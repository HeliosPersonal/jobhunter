using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T05: the jobs persistence — the conflict-tolerant insert reports insert-vs-conflict in one round trip
/// (invariant 2), the unique fingerprint index rejects a duplicate, <c>LiveJobsQuery</c> excludes closed
/// and quarantined jobs and is served by the partial index (asserted with a query plan), and
/// <c>job_aliases</c> has no delete path. Requires Docker.
/// </summary>
public sealed class JobPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, "job-1", ContentHash.Compute("{\"t\":\"x\"}"), "{\"t\":\"x\"}", 200, Now));
        await ctx.SaveChangesAsync();

        return new Seed(database, companyId, sourceId, rawPostingId);
    }

    private static Job BuildJob(Seed seed, string fingerprintHex, string title = "Staff SRE")
    {
        var fingerprint = Fingerprint.TryCreate(fingerprintHex).Value;
        var locations = LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]);
        var salary = SalaryRange.TryCreate(120_000m, 160_000m, "USD", SalaryPeriod.Year).Value;

        var job = new Job(
            Guid.CreateVersion7(),
            seed.CompanyId,
            seed.RawPostingId,
            fingerprint,
            fingerprintVersion: 1,
            title,
            normalisedTitle: title.ToLowerInvariant(),
            description: "We run reliable systems.",
            applyUrl: "https://acme.com/apply/1",
            locations,
            RemotePolicy.Hybrid,
            EmploymentType.FullTime,
            PostedAtGranularity.Day,
            firstSeenAt: Now,
            lastSeenAt: Now,
            seniority: Seniority.Staff,
            salary: salary,
            salaryRaw: "$120,000 - $160,000",
            postedAt: Now.AddDays(-1));

        job.RegisterAlias(seed.RawPostingId, seed.SourceId, Now, Now);
        job.AddTechnology("C#", TechnologyMatch.Title);
        return job;
    }

    private static string Hex(char c) => new(c, 64);

    [RequiresDockerFact]
    public async Task First_insert_reports_inserted_and_persists_the_aggregate_with_its_children()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        var job = BuildJob(seed, Hex('a'));
        (await repo.InsertAsync(job)).ShouldBe(JobInsertOutcome.Inserted);

        await using var read = seed.Database.CreateContext();
        var stored = await read.Set<Job>()
            .Include(j => j.Aliases)
            .Include(j => j.Technologies)
            .SingleAsync(j => j.Id == job.Id);

        stored.Title.ShouldBe("Staff SRE");
        stored.Fingerprint.Value.ShouldBe(Hex('a'));
        stored.Salary!.Min.ShouldBe(120_000m);
        stored.Salary.Currency.ShouldBe("USD");
        stored.Locations.Locations[0].City.ShouldBe("Berlin");
        stored.Aliases.ShouldHaveSingleItem().RawPostingId.ShouldBe(seed.RawPostingId);
        stored.Technologies.ShouldHaveSingleItem().Technology.ShouldBe("C#");
    }

    [RequiresDockerFact]
    public async Task A_second_insert_on_the_same_fingerprint_reports_conflict_and_writes_nothing()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        (await repo.InsertAsync(BuildJob(seed, Hex('b')))).ShouldBe(JobInsertOutcome.Inserted);

        var second = BuildJob(seed, Hex('b'), title: "Principal SRE");
        (await repo.InsertAsync(second)).ShouldBe(JobInsertOutcome.FingerprintConflict);

        await using var read = seed.Database.CreateContext();
        // Exactly one job, and the conflicting insert wrote no orphan alias for its (unused) job id.
        (await read.Set<Job>().CountAsync()).ShouldBe(1);
        (await read.Set<Job>().SingleAsync()).Title.ShouldBe("Staff SRE");
        (await read.Set<JobAlias>().CountAsync(a => a.JobId == second.Id)).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task The_conflict_path_loads_the_winning_job_by_fingerprint_for_a_new_alias()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        var winner = BuildJob(seed, Hex('c'));
        await repo.InsertAsync(winner);

        // A second board carries the same opening; the second raw posting seeds a fresh alias.
        var secondPostingId = Guid.CreateVersion7();
        await using (var ctx = seed.Database.CreateContext())
        {
            ctx.Add(new RawPosting(secondPostingId, seed.SourceId, "job-2", ContentHash.Compute("{\"t\":\"y\"}"), "{\"t\":\"y\"}", 200, Now));
            await ctx.SaveChangesAsync();
        }

        var loaded = await repo.FindByFingerprintAsync(Fingerprint.TryCreate(Hex('c')).Value);
        loaded.ShouldNotBeNull();
        loaded!.RegisterAlias(secondPostingId, seed.SourceId, Now, Now.AddHours(6));
        await repo.SaveChangesAsync();

        await using var read = seed.Database.CreateContext();
        var aliasCount = await read.Set<JobAlias>().CountAsync(a => a.JobId == winner.Id);
        aliasCount.ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task The_unique_fingerprint_index_rejects_a_direct_duplicate_insert()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await using var connection = new Npgsql.NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        // Insert one job row directly, then violate the unique index with a second row of the same fingerprint.
        var repo = NewRepository(seed);
        await repo.InsertAsync(BuildJob(seed, Hex('d')));

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO jobs
                (id, company_id, origin_raw_posting_id, fingerprint, fingerprint_version, title,
                 normalised_title, description, apply_url, locations, remote_policy, employment_type,
                 posted_at_granularity, first_seen_at, last_seen_at, status, is_tier2)
            VALUES
                (@id, @company, @origin, @fp, 1, 't', 't', 'd', 'u', '[]', 'Remote', 'FullTime',
                 'Day', @now, @now, 'Live', false);
            """;
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("company", seed.CompanyId);
        command.Parameters.AddWithValue("origin", seed.RawPostingId);
        command.Parameters.AddWithValue("fp", Hex('d'));
        command.Parameters.AddWithValue("now", Now);

        var ex = await Should.ThrowAsync<Npgsql.PostgresException>(() => command.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.UniqueViolation);
    }

    [RequiresDockerFact]
    public async Task LiveJobsQuery_returns_live_jobs_since_the_cutoff_and_excludes_closed_and_quarantined()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        var live = BuildJob(seed, Hex('a'), "Live Role");
        var closed = BuildJob(seed, Hex('b'), "Closed Role");
        var quarantined = BuildJob(seed, Hex('c'), "Quarantined Role");
        await repo.InsertAsync(live);
        await repo.InsertAsync(closed);
        await repo.InsertAsync(quarantined);

        var toClose = await repo.FindAsync(closed.Id);
        toClose!.Close(Now.AddHours(1));
        var toQuarantine = await repo.FindAsync(quarantined.Id);
        toQuarantine!.Quarantine();
        await repo.SaveChangesAsync();

        var query = new LiveJobsQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var rows = await query.DiscoveredSinceAsync(Now.AddHours(-1));

        rows.ShouldHaveSingleItem().Title.ShouldBe("Live Role");
    }

    [RequiresDockerFact]
    public async Task LiveJobsQuery_excludes_jobs_first_seen_before_the_cutoff()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);
        await repo.InsertAsync(BuildJob(seed, Hex('a')));

        var query = new LiveJobsQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var rows = await query.DiscoveredSinceAsync(Now.AddDays(1));

        rows.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task LiveJobsQuery_is_served_by_the_partial_first_seen_index()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);
        await repo.InsertAsync(BuildJob(seed, Hex('a')));

        await using var connection = new Npgsql.NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        // Force an index scan so the planner's choice reflects index availability, not table size.
        await using (var settings = connection.CreateCommand())
        {
            settings.CommandText = "SET enable_seqscan = off;";
            await settings.ExecuteNonQueryAsync();
        }

        await using var explain = connection.CreateCommand();
        explain.CommandText =
            "EXPLAIN SELECT id FROM jobs WHERE status = 'Live' AND first_seen_at >= @since ORDER BY first_seen_at DESC";
        explain.Parameters.AddWithValue("since", Now.AddHours(-1));

        var plan = new System.Text.StringBuilder();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.AppendLine(reader.GetString(0));
            }
        }

        plan.ToString().ShouldContain("idx_jobs_first_seen");
    }

    [RequiresDockerFact]
    public async Task The_trigram_index_and_extension_exist_after_migration()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        await using var connection = new Npgsql.NpgsqlConnection(seed.Database.ConnectionString);
        await connection.OpenAsync();

        await using var extension = connection.CreateCommand();
        extension.CommandText = "SELECT COUNT(*) FROM pg_extension WHERE extname = 'pg_trgm'";
        ((long)(await extension.ExecuteScalarAsync() ?? 0L)).ShouldBe(1);

        await using var index = connection.CreateCommand();
        index.CommandText =
            "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'idx_jobs_normalised_title_trgm'";
        ((long)(await index.ExecuteScalarAsync() ?? 0L)).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task CompanyJobsQuery_returns_only_the_companys_live_jobs_and_excludes_the_closed()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        await repo.InsertAsync(BuildJob(seed, Hex('a'), "First Role"));
        await repo.InsertAsync(BuildJob(seed, Hex('b'), "Second Role"));
        var closed = BuildJob(seed, Hex('c'), "Closed Role");
        await repo.InsertAsync(closed);

        var toClose = await repo.FindAsync(closed.Id);
        toClose!.Close(Now.AddHours(1));
        await repo.SaveChangesAsync();

        var query = new CompanyJobsQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var rows = await query.LiveForCompanyAsync(seed.CompanyId);

        rows.Select(r => r.Title).ShouldBe(["First Role", "Second Role"], ignoreOrder: true);
        rows.ShouldNotContain(r => r.Title == "Closed Role");
    }

    [RequiresDockerFact]
    public async Task CompanyJobsQuery_returns_nothing_for_a_company_with_no_live_jobs()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var query = new CompanyJobsQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var rows = await query.LiveForCompanyAsync(Guid.NewGuid());

        rows.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task LiveJobCountQuery_counts_only_live_jobs()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        await repo.InsertAsync(BuildJob(seed, Hex('a'), "Live One"));
        await repo.InsertAsync(BuildJob(seed, Hex('b'), "Live Two"));
        var closed = BuildJob(seed, Hex('c'), "Closed Role");
        await repo.InsertAsync(closed);
        var toClose = await repo.FindAsync(closed.Id);
        toClose!.Close(Now.AddHours(1));
        await repo.SaveChangesAsync();

        var query = new LiveJobCountQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        (await query.CountLiveAsync()).ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task JobProjectionQuery_projects_a_live_job_with_its_company_technologies_and_countries()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        var job = BuildJob(seed, Hex('a'), "Staff SRE");
        await repo.InsertAsync(job);

        var query = new JobProjectionQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var source = await query.ProjectAsync(job.Id);

        source.ShouldNotBeNull();
        source!.Id.ShouldBe(job.Id);
        source.Title.ShouldBe("Staff SRE");
        source.Status.ShouldBe("Live");
        source.CompanyName.ShouldBe("Acme");
        source.CompanyDomain.ShouldBe("acme.com");
        source.Technologies.ShouldContain("C#");
        source.Countries.ShouldBe(["Germany"]);
        source.RemotePolicy.ShouldBe("Hybrid");
        source.EmploymentType.ShouldBe("FullTime");
        source.Seniority.ShouldBe("Staff");
        source.SalaryMin.ShouldBe(120_000);
        source.SalaryMax.ShouldBe(160_000);
        source.SalaryCurrency.ShouldBe("USD");
        // F3/F4/F6 columns are not present until those features merge — the projection carries null.
        source.CompanyStage.ShouldBeNull();
        source.AiUsage.ShouldBeNull();
        source.Score.ShouldBeNull();
        source.ApplicationStatus.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task JobProjectionQuery_returns_null_for_a_closed_job_so_the_indexer_deletes_it()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        var job = BuildJob(seed, Hex('a'));
        await repo.InsertAsync(job);
        var toClose = await repo.FindAsync(job.Id);
        toClose!.Close(Now.AddHours(1));
        await repo.SaveChangesAsync();

        var query = new JobProjectionQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        (await query.ProjectAsync(job.Id)).ShouldBeNull();
        (await query.ProjectAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task JobProjectionQuery_streams_every_live_job_ordered_by_id_for_a_rebuild()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var repo = NewRepository(seed);

        await repo.InsertAsync(BuildJob(seed, Hex('a'), "First"));
        await repo.InsertAsync(BuildJob(seed, Hex('b'), "Second"));
        var closed = BuildJob(seed, Hex('c'), "Closed");
        await repo.InsertAsync(closed);
        var toClose = await repo.FindAsync(closed.Id);
        toClose!.Close(Now.AddHours(1));
        await repo.SaveChangesAsync();

        var query = new JobProjectionQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        var streamed = new List<Guid>();
        await foreach (var source in query.ProjectLiveAsync())
        {
            source.Status.ShouldBe("Live");
            streamed.Add(source.Id);
        }

        streamed.Count.ShouldBe(2);
        streamed.ShouldBe(streamed.OrderBy(id => id).ToList());
    }

    private static JobRepository NewRepository(Seed seed) =>
        new(seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));

    private sealed record Seed(TestDatabase Database, Guid CompanyId, Guid SourceId, Guid RawPostingId);
}
