using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T10: the read side of "the job's facts at the moment of the tap" (AC-08, F7 data-model §signals). The
/// query projects a <see cref="Job"/> and its <em>latest</em> <see cref="Enrichment"/> into the
/// <see cref="JobFacts"/> vocabulary the preference learner keys on — salary band, country, company size,
/// technologies, timezone band, remote policy, employment type. The load-bearing properties: the salary
/// band is derived (there is no source column), a superseded enrichment does not supply company size or
/// timezone (the latest wins), a job with no enrichment still snapshots its own columns, and a closed or
/// missing job returns <c>null</c> so the caller records nothing invalid (AC-09). Read-only — Dapper never
/// writes (architecture rule 4). Requires Docker.
/// </summary>
public sealed class JobFactsSnapshotQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Snapshot_projects_the_job_and_its_latest_enrichment_into_job_facts()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await SeedEnrichmentAsync(
            seed, CompanyStage.SeriesA, TimezoneBand.AMER, seed.RunId, Now.AddHours(-2),
            AiUsageLevel.Low, RoleFamily.BackendGeneric);
        // A later enrichment supersedes the earlier one — the snapshot reads this, not the stale row.
        await SeedSecondEnrichmentAsync(
            seed, CompanyStage.SeriesB, TimezoneBand.EMEA, Now, AiUsageLevel.High, RoleFamily.AiPlatform);

        var query = new JobFactsSnapshotQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var facts = await query.SnapshotAsync(seed.JobId);

        facts.ShouldNotBeNull();
        // 165k USD annual midpoint bands to 150-180k (F7 data-model example).
        facts!.ValuesFor(Dimension.SalaryBand).ShouldBe(["150-180k"]);
        facts.ValuesFor(Dimension.Country).ShouldBe(["Germany"]);
        facts.ValuesFor(Dimension.CompanySize).ShouldBe(["SeriesB"]);
        facts.ValuesFor(Dimension.Technology).ShouldBe(["C#", "Kafka"]);
        facts.ValuesFor(Dimension.TimezoneBand).ShouldBe(["EMEA"]);
        facts.ValuesFor(Dimension.RemotePolicy).ShouldBe(["Remote"]);
        facts.ValuesFor(Dimension.EmploymentType).ShouldBe(["FullTime"]);
        // T10 (TUNE-08): the career-trajectory dimensions come from the latest enrichment too.
        facts.ValuesFor(Dimension.AiUsage).ShouldBe(["High"]);
        facts.ValuesFor(Dimension.RoleFamily).ShouldBe(["AiPlatform"]);
    }

    [RequiresDockerFact]
    public async Task Snapshot_of_a_job_with_no_enrichment_carries_only_its_own_columns()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var query = new JobFactsSnapshotQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var facts = await query.SnapshotAsync(seed.JobId);

        facts.ShouldNotBeNull();
        // No enrichment -> no company size, no timezone band, no AI usage or role family; the job's own
        // columns still snapshot.
        facts!.ValuesFor(Dimension.CompanySize).ShouldBeEmpty();
        facts.ValuesFor(Dimension.TimezoneBand).ShouldBeEmpty();
        facts.ValuesFor(Dimension.AiUsage).ShouldBeEmpty();
        facts.ValuesFor(Dimension.RoleFamily).ShouldBeEmpty();
        facts.ValuesFor(Dimension.RemotePolicy).ShouldBe(["Remote"]);
        facts.ValuesFor(Dimension.EmploymentType).ShouldBe(["FullTime"]);
        facts.ValuesFor(Dimension.SalaryBand).ShouldBe(["150-180k"]);
    }

    [RequiresDockerFact]
    public async Task Snapshot_leaves_a_non_usd_salary_unbanded_rather_than_converting_it()
    {
        var seed = await SeedAsync(salaryCurrency: "EUR");
        await using var _ = seed.Database;

        var query = new JobFactsSnapshotQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var facts = await query.SnapshotAsync(seed.JobId);

        facts.ShouldNotBeNull();
        facts!.ValuesFor(Dimension.SalaryBand).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task Snapshot_of_a_closed_job_is_null()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        await using (var ctx = seed.Database.CreateContext())
        {
            var job = await ctx.FindAsync<Job>(seed.JobId);
            job!.Close(Now);
            await ctx.SaveChangesAsync();
        }

        var query = new JobFactsSnapshotQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));

        // A job closed between delivery and the tap is unavailable — nothing invalid is recorded (AC-09).
        (await query.SnapshotAsync(seed.JobId)).ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task Snapshot_of_a_missing_job_is_null()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var query = new JobFactsSnapshotQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.SnapshotAsync(Guid.CreateVersion7())).ShouldBeNull();
    }

    private static async Task SeedEnrichmentAsync(
        Seed seed, CompanyStage stage, TimezoneBand band, Guid runId, DateTimeOffset createdAt,
        AiUsageLevel aiUsage = AiUsageLevel.None, RoleFamily roleFamily = RoleFamily.Platform)
    {
        var enrichment = new Enrichment(
            Guid.CreateVersion7(), seed.JobId, runId, salary: null,
            isRemote: true, isContractorFriendly: false, band, aiUsage, AiSignals.None,
            stage, roleFamily, technologies: [], reasons: ["Assessed."], "enrich-v1", createdAt);
        var repo = new EnrichmentRepository(
            seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await repo.UpsertAsync(enrichment);
    }

    private static async Task SeedSecondEnrichmentAsync(
        Seed seed, CompanyStage stage, TimezoneBand band, DateTimeOffset createdAt,
        AiUsageLevel aiUsage = AiUsageLevel.None, RoleFamily roleFamily = RoleFamily.Platform)
    {
        // A second enrichment needs a second, terminal Run (the single-active-run index).
        var secondRun = new Run(Guid.CreateVersion7(), Now, Now.AddDays(1), 5m, Now.AddDays(1));
        await using (var ctx = seed.Database.CreateContext())
        {
            var first = await ctx.FindAsync<Run>(seed.RunId);
            first!.Abort("done", Now.AddHours(1), costBreach: false);
            ctx.Add(secondRun);
            await ctx.SaveChangesAsync();
        }

        await SeedEnrichmentAsync(seed, stage, band, secondRun.Id, createdAt, aiUsage, roleFamily);
    }

    private static async Task<Seed> SeedAsync(string salaryCurrency = "USD")
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

        var job = new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: Now, lastSeenAt: Now,
            salary: SalaryRange.TryCreate(150_000m, 180_000m, salaryCurrency, SalaryPeriod.Year).Value);
        job.AddTechnology("Kafka", TechnologyMatch.Vocabulary);
        job.AddTechnology("C#", TechnologyMatch.Vocabulary);
        ctx.Add(job);
        ctx.Add(new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now));
        await ctx.SaveChangesAsync();

        return new Seed(database, jobId, runId);
    }

    private sealed record Seed(TestDatabase Database, Guid JobId, Guid RunId);
}
