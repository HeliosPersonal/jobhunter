using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F7 T08 C5: the read side of <c>/hidden</c> (done-when 6, risk D3). It lists the jobs the most recent Run
/// suppressed, each with the reason it was withheld — so suppression regret is visible and measurable, not
/// silent (invariant 11). Only suppressed scores of the latest Run are returned, best-score first, capped at
/// the caller's limit; a shown score never appears, and an earlier Run's suppressions do not. It selects
/// nothing about the Owner's CV. Requires Docker.
/// </summary>
public sealed class HiddenJobsQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task It_lists_a_suppressed_job_with_its_reason_and_score()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, runId, 30m, suppressed: true, reason: "Salary below 170k EUR");

        var query = new HiddenJobsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var hidden = await query.HiddenAsync(limit: 10);

        var job = hidden.ShouldHaveSingleItem();
        job.JobId.ShouldBe(jobId);
        job.Title.ShouldBe("Staff SRE");
        job.Company.ShouldBe("Acme");
        job.SuppressionReason.ShouldBe("Salary below 170k EUR");
        job.Score.ShouldBe(30m);
    }

    [RequiresDockerFact]
    public async Task A_shown_job_never_appears()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var shown = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, shown, runId, 82m, suppressed: false);

        var query = new HiddenJobsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.HiddenAsync(limit: 10)).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task Only_the_latest_run_is_shown_so_an_older_suppression_does_not_linger()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var oldRun = await SeedRunAsync(database, RunStart.AddDays(-1));
        var newRun = await SeedRunAsync(database, RunStart);
        var oldJob = await SeedJobAsync(database, companyId);
        var newJob = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, oldJob, oldRun, 20m, suppressed: true, reason: "Old reason");
        await SeedScoreAsync(database, newJob, newRun, 25m, suppressed: true, reason: "New reason");

        var query = new HiddenJobsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var hidden = await query.HiddenAsync(limit: 10);

        hidden.ShouldHaveSingleItem().JobId.ShouldBe(newJob);
    }

    [RequiresDockerFact]
    public async Task Suppressed_jobs_come_back_best_score_first_and_capped()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var low = await SeedJobAsync(database, companyId);
        var high = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, low, runId, 20m, suppressed: true, reason: "Low");
        await SeedScoreAsync(database, high, runId, 45m, suppressed: true, reason: "High");

        var query = new HiddenJobsQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.HiddenAsync(limit: 10)).Select(h => h.JobId).ShouldBe([high, low]);
        (await query.HiddenAsync(limit: 1)).Select(h => h.JobId).ShouldBe([high]);
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, RunStart));
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static async Task<Guid> SeedRunAsync(TestDatabase database, DateTimeOffset startedAt)
    {
        var runId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var run = new Run(runId, startedAt.AddDays(-1), startedAt, ceilingUsd: 5m, startedAt);
        // Retire immediately so the single-active-run partial index allows more than one seeded run.
        run.Abort("seeded", startedAt.AddMinutes(1), costBreach: false);
        ctx.Add(run);
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static async Task<Guid> SeedJobAsync(TestDatabase database, Guid companyId)
    {
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", FirstSeen));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, FirstSeen));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N") + Guid.NewGuid().ToString("N")).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "We keep the lights on.",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: FirstSeen, lastSeenAt: FirstSeen, salary: null, status: JobStatus.Live));
        await ctx.SaveChangesAsync();
        return jobId;
    }

    private static async Task SeedScoreAsync(
        TestDatabase database, Guid jobId, Guid runId, decimal finalScore, bool suppressed, string? reason = null)
    {
        var fraction = finalScore / 100m;
        var components = new ScoreComponents(
            match: fraction, alignment: fraction, preference: fraction, freshness: fraction,
            confidenceMultiplier: 1.00m);
        var score = new Score(
            jobId, runId, finalScore, components, RankingWeights.Default, preferenceModelId: null,
            suppressed, reason, RunStart);

        var repo = new ScoreRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.UpsertAsync(score);
    }
}
