using System.Text;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Research;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T02: the F8 persistence. The migration creates <c>company_research</c>, <c>research_sources</c> and
/// <c>research_claims</c> with all six declared indexes. The load-bearing detail is invariant 5 expressed in
/// the schema: <c>research_claims.source_id</c> is <c>NOT NULL</c>, and a composite foreign key
/// <c>(research_id, source_id)</c> to <c>research_sources(research_id, id)</c> makes a claim citing a source
/// from another dossier unrepresentable — an uncited claim is not merely rejected by application logic, it
/// cannot be inserted. One dossier per <c>(company, run)</c> is <c>uq_research_company_run</c>, and the latest
/// dossier / freshness read is covered by <c>idx_research_company_latest</c>, verified with a query plan.
/// Requires Docker.
/// </summary>
public sealed class ResearchPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedIndexes =
    [
        "uq_research_company_run",
        "idx_research_company_latest",
        "idx_sources_research",
        "uq_sources_url",
        "idx_claims_research",
        "idx_claims_warnings",
    ];

    [RequiresDockerFact]
    public async Task All_six_declared_indexes_exist_after_the_migration()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND indexname = ANY(@names)";
        command.Parameters.AddWithValue("names", ExpectedIndexes);

        var found = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetString(0));
        }

        found.OrderBy(x => x).ShouldBe(ExpectedIndexes.OrderBy(x => x));
    }

    [RequiresDockerFact]
    public async Task A_dossier_round_trips_with_its_sources_and_claims()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var source = NewSource();
        var research = NewResearch(seed, [source], [NewClaim(source)]);
        var repo = new ResearchRepository(seed.Database.CreateContext());
        repo.Add(research);
        await repo.SaveChangesAsync();

        var read = new ResearchRepository(seed.Database.CreateContext());
        var loaded = await read.FindLatestAsync(seed.CompanyId);

        loaded.ShouldNotBeNull();
        loaded!.CompanyId.ShouldBe(seed.CompanyId);
        loaded.RunId.ShouldBe(seed.RunId);
        loaded.Summary.ShouldBe("A mid-stage company with an active engineering blog.");
        loaded.ClaimsDiscarded.ShouldBe(2);
        loaded.Sources.ShouldHaveSingleItem().Url.ShouldBe("https://example.com/blog");
        var claim = loaded.Claims.ShouldHaveSingleItem();
        claim.Claim.ShouldBe("Runs a well-documented engineering blog.");
        claim.SourceId.ShouldBe(source.Id);
        claim.ObservedAt.ShouldBe(source.ObservedAt);
        loaded.CategoriesUnavailable.ShouldBe([ResearchCategory.Layoffs, ResearchCategory.Funding]);
        loaded.CategoriesCovered.ShouldBe([ResearchCategory.EngineeringBlog]);
    }

    [RequiresDockerFact]
    public async Task FindLatest_returns_the_newest_dossier_for_the_company()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var laterRunId = await SeedRunAsync(seed.Database, Now.AddDays(1));

        var olderSource = NewSource();
        var older = NewResearch(seed, [olderSource], [NewClaim(olderSource)], generatedAt: Now);
        var newerSource = NewSource();
        var newer = new CompanyResearch(
            Guid.CreateVersion7(), seed.CompanyId, laterRunId, "A later dossier.",
            [newerSource], [NewClaim(newerSource)], [], 0, "research-v1", Now.AddDays(1));

        var write = new ResearchRepository(seed.Database.CreateContext());
        write.Add(older);
        write.Add(newer);
        await write.SaveChangesAsync();

        var loaded = await new ResearchRepository(seed.Database.CreateContext()).FindLatestAsync(seed.CompanyId);

        loaded.ShouldNotBeNull();
        loaded!.Summary.ShouldBe("A later dossier.");
    }

    [RequiresDockerFact]
    public async Task FindLatest_returns_null_when_the_company_has_never_been_researched()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var loaded = await new ResearchRepository(seed.Database.CreateContext())
            .FindLatestAsync(Guid.CreateVersion7());

        loaded.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task A_claim_with_a_null_source_is_rejected_by_the_database()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var researchId = await SeedEmptyDossierAsync(seed);

        // A raw insert with a null source_id proves the schema, not the aggregate, is the arbiter of
        // invariant 5: an uncited claim is unrepresentable at the row level.
        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO research_claims (id, research_id, source_id, category, claim, is_warning, observed_at) " +
            "VALUES (@id, @research_id, NULL, @category, @claim, false, @observed_at)";
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("research_id", researchId);
        insert.Parameters.AddWithValue("category", ResearchCategory.News.ToString());
        insert.Parameters.AddWithValue("claim", "An uncited assertion.");
        insert.Parameters.AddWithValue("observed_at", Now);

        var ex = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe(PostgresErrorCodes.NotNullViolation);
    }

    [RequiresDockerFact]
    public async Task A_claim_citing_a_source_from_another_dossier_is_rejected_by_the_foreign_key()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var laterRunId = await SeedRunAsync(seed.Database, Now.AddDays(1));

        // Dossier A owns the source; dossier B does not. A claim in B citing A's source is a source from a
        // different dossier — the composite (research_id, source_id) foreign key must reject it.
        var sourceA = NewSource();
        var dossierA = NewResearch(seed, [sourceA], [NewClaim(sourceA)]);
        var dossierBId = Guid.CreateVersion7();
        var dossierB = new CompanyResearch(
            dossierBId, seed.CompanyId, laterRunId, "Dossier B.", [], [], [], 0, "research-v1", Now.AddDays(1));

        var write = new ResearchRepository(seed.Database.CreateContext());
        write.Add(dossierA);
        write.Add(dossierB);
        await write.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO research_claims (id, research_id, source_id, category, claim, is_warning, observed_at) " +
            "VALUES (@id, @research_id, @source_id, @category, @claim, false, @observed_at)";
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("research_id", dossierBId);
        insert.Parameters.AddWithValue("source_id", sourceA.Id);
        insert.Parameters.AddWithValue("category", ResearchCategory.EngineeringBlog.ToString());
        insert.Parameters.AddWithValue("claim", "A claim resting on another dossier's source.");
        insert.Parameters.AddWithValue("observed_at", sourceA.ObservedAt);

        var ex = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [RequiresDockerFact]
    public async Task A_second_dossier_for_the_same_company_and_run_is_rejected()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var first = NewResearch(seed, [], []);
        var firstRepo = new ResearchRepository(seed.Database.CreateContext());
        firstRepo.Add(first);
        await firstRepo.SaveChangesAsync();

        var second = NewResearch(seed, [], []);
        var secondRepo = new ResearchRepository(seed.Database.CreateContext());
        secondRepo.Add(second);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => secondRepo.SaveChangesAsync());
        var pg = ex.InnerException.ShouldBeOfType<PostgresException>();
        pg.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.ShouldBe("uq_research_company_run");
    }

    [RequiresDockerFact]
    public async Task The_latest_dossier_lookup_is_covered_by_its_index()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = new ResearchRepository(seed.Database.CreateContext());
        repo.Add(NewResearch(seed, [], []));
        await repo.SaveChangesAsync();

        var plan = await ExplainAsync(
            seed,
            "SELECT id FROM company_research WHERE company_id = @company ORDER BY generated_at DESC LIMIT 1",
            ("company", seed.CompanyId));

        plan.ShouldContain("idx_research_company_latest");
    }

    private static ResearchSource NewSource() =>
        new(Guid.CreateVersion7(), ResearchCategory.EngineeringBlog, "https://example.com/blog", "Blog", 3000, Now.AddHours(-1));

    private static ResearchClaim NewClaim(ResearchSource source) =>
        new(Guid.CreateVersion7(), source, ResearchCategory.EngineeringBlog, "Runs a well-documented engineering blog.", isWarning: false);

    private static CompanyResearch NewResearch(
        Seed seed,
        IReadOnlyList<ResearchSource> sources,
        IReadOnlyList<ResearchClaim> claims,
        DateTimeOffset? generatedAt = null) =>
        new(
            Guid.CreateVersion7(),
            seed.CompanyId,
            seed.RunId,
            "A mid-stage company with an active engineering blog.",
            sources,
            claims,
            [ResearchCategory.Layoffs, ResearchCategory.Funding],
            claimsDiscarded: 2,
            "research-v1",
            generatedAt ?? Now);

    private static async Task<Guid> SeedEmptyDossierAsync(Seed seed)
    {
        var research = NewResearch(seed, [], []);
        var repo = new ResearchRepository(seed.Database.CreateContext());
        repo.Add(research);
        await repo.SaveChangesAsync();
        return research.Id;
    }

    private static async Task<string> ExplainAsync(
        Seed seed,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        await using (var noSeq = connection.CreateCommand())
        {
            noSeq.CommandText = "SET enable_seqscan = off;";
            await noSeq.ExecuteNonQueryAsync();
        }

        await using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN " + sql;
        foreach (var (name, value) in parameters)
        {
            explain.Parameters.AddWithValue(name, value);
        }

        var plan = new StringBuilder();
        await using var reader = await explain.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.AppendLine(reader.GetString(0));
        }

        return plan.ToString();
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        await ctx.SaveChangesAsync();

        var runId = await SeedRunAsync(database, Now);
        return new Seed(database, companyId, runId);
    }

    private static async Task<Guid> SeedRunAsync(TestDatabase database, DateTimeOffset startedAt)
    {
        var runId = Guid.CreateVersion7();
        var run = new Run(runId, startedAt.AddHours(-24), startedAt, 2.00m, startedAt);
        // Terminal, so a test that seeds two Runs does not trip uq_runs_single_active (F3): at most one live
        // Run may exist. A finished Run is exactly what a dossier from a past day references.
        run.Abort("seeded finished for the F8 persistence test", startedAt, costBreach: false);

        await using var ctx = database.CreateContext();
        ctx.Add(run);
        await ctx.SaveChangesAsync();
        return runId;
    }

    private sealed record Seed(TestDatabase Database, Guid CompanyId, Guid RunId);
}
