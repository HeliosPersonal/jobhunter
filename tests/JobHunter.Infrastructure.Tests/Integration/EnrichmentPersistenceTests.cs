using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T05: the enrichment persistence. The upsert keyed on <c>(job_id, run_id)</c> writes once and is a
/// no-op on replay (AC-06); the unique <c>uq_enrichments_job_run</c> index carries invariant 3, asserted
/// by violating it directly; <c>reasons</c> round-trips as a JSON array and cannot be empty at the domain
/// boundary (invariant 4); <c>prompt_version</c> is non-null on every row (AC-11). Requires Docker.
/// </summary>
public sealed class EnrichmentPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static Enrichment BuildEnrichment(Guid jobId, Guid runId, string promptVersion = "enrich-v1") =>
        new(
            Guid.CreateVersion7(), jobId, runId,
            SalaryEstimate.TryCreate(120_000m, 160_000m, "USD", SalaryPeriod.Year, 0.7m).Value,
            isRemote: true, isContractorFriendly: false, TimezoneBand.EMEA, AiUsageLevel.Medium,
            new AiSignals(buildsAiProduct: false, buildsAiInfra: true, usesAiTooling: true, isResearch: false),
            CompanyStage.SeriesB, RoleFamily.Platform, technologies: ["C#", ".NET"],
            reasons: ["Salary band inferred from peers."],
            promptVersion, Now);

    [RequiresDockerFact]
    public async Task Upsert_persists_the_enrichment_with_its_salary_reasons_and_technologies()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        var enrichment = BuildEnrichment(seed.JobId, seed.RunId);
        (await repo.UpsertAsync(enrichment)).ShouldBeTrue();

        await using var read = seed.Database.CreateContext();
        var stored = await read.Set<Enrichment>().SingleAsync();
        stored.JobId.ShouldBe(seed.JobId);
        stored.RunId.ShouldBe(seed.RunId);
        stored.Salary!.Min.ShouldBe(120_000m);
        stored.Salary.Confidence.ShouldBe(0.7m);
        stored.TimezoneBand.ShouldBe(TimezoneBand.EMEA);
        stored.RoleFamily.ShouldBe(RoleFamily.Platform);
        stored.AiSignals.BuildsAiInfra.ShouldBeTrue();
        stored.AiSignals.UsesAiTooling.ShouldBeTrue();
        stored.AiSignals.BuildsAiProduct.ShouldBeFalse();
        stored.AiSignals.IsResearch.ShouldBeFalse();
        stored.Reasons.ShouldHaveSingleItem().ShouldBe("Salary band inferred from peers.");
        stored.Technologies.ShouldBe(["C#", ".NET"]);
        stored.PromptVersion.ShouldBe("enrich-v1");
    }

    [RequiresDockerFact]
    public async Task Upsert_is_idempotent_a_second_call_on_the_same_key_leaves_one_row_unchanged()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        (await repo.UpsertAsync(BuildEnrichment(seed.JobId, seed.RunId))).ShouldBeTrue();
        // A replay of the same (job_id, run_id) — a different aggregate id, same key — is a no-op.
        (await repo.UpsertAsync(BuildEnrichment(seed.JobId, seed.RunId))).ShouldBeFalse();

        await using var read = seed.Database.CreateContext();
        (await read.Set<Enrichment>().CountAsync()).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task The_unique_job_run_index_rejects_a_direct_duplicate_insert()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);
        await repo.UpsertAsync(BuildEnrichment(seed.JobId, seed.RunId));

        await using var connection = new Npgsql.NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO enrichments
                (id, job_id, run_id, is_remote, is_contractor_friendly, timezone_band, ai_usage,
                 ai_builds_product, ai_builds_infra, ai_uses_tooling, ai_is_research,
                 company_stage, role_family, technologies, reasons, prompt_version, created_at)
            VALUES
                (@id, @job, @run, true, false, 'EMEA', 'Medium', false, false, false, false,
                 'SeriesB', 'Platform', '[]', '["x"]', 'v1', @now);
            """;
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("job", seed.JobId);
        command.Parameters.AddWithValue("run", seed.RunId);
        command.Parameters.AddWithValue("now", Now);

        var ex = await Should.ThrowAsync<Npgsql.PostgresException>(() => command.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.UniqueViolation);
        ex.ConstraintName.ShouldBe("uq_enrichments_job_run");
    }

    [RequiresDockerFact]
    public async Task An_enrichment_with_no_reason_cannot_be_constructed_at_the_domain_boundary()
    {
        // Invariant 4 is a type-level property, so it never reaches the database: construction throws.
        Should.Throw<ArgumentException>(() => new Enrichment(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), salary: null,
            isRemote: true, isContractorFriendly: false, TimezoneBand.Global, AiUsageLevel.None,
            AiSignals.None, CompanyStage.Unknown, RoleFamily.Other, technologies: [], reasons: ["   "],
            promptVersion: "v1", Now));

        await Task.CompletedTask;
    }

    [RequiresDockerFact]
    public async Task A_second_run_can_re_enrich_the_same_job_as_a_distinct_row()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);
        await repo.UpsertAsync(BuildEnrichment(seed.JobId, seed.RunId));

        // A correction is a new row for a new Run, guaranteed distinct by the (job_id, run_id) index.
        var secondRun = new Run(Guid.CreateVersion7(), Now, Now.AddDays(1), 5m, Now.AddDays(1));
        var runRepo = new RunRepository(seed.Database.CreateContext());
        // First Run must be terminal so the single-active index allows the second.
        var first = await runRepo.FindAsync(seed.RunId);
        first!.Abort("done", Now.AddHours(1), costBreach: false);
        await runRepo.SaveChangesAsync();
        var runRepo2 = new RunRepository(seed.Database.CreateContext());
        runRepo2.Add(secondRun);
        await runRepo2.SaveChangesAsync();

        (await repo.UpsertAsync(BuildEnrichment(seed.JobId, secondRun.Id))).ShouldBeTrue();

        await using var read = seed.Database.CreateContext();
        (await read.Set<Enrichment>().CountAsync(e => e.JobId == seed.JobId)).ShouldBe(2);
    }

    private static EnrichmentRepository NewRepository(Seed seed) =>
        new(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, "job-1", ContentHash.Compute("{\"t\":\"x\"}"), "{\"t\":\"x\"}", 200, Now));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1", LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: Now, lastSeenAt: Now));
        ctx.Add(new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now));
        await ctx.SaveChangesAsync();

        return new Seed(database, jobId, runId);
    }

    private sealed record Seed(TestDatabase Database, Guid JobId, Guid RunId);
}
