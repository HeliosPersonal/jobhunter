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

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F7 T09 done-when 5 (risk D3): suppression regret — the latest Run's suppressed jobs the Owner then acted
/// on. A job the learned model hid, retrieved through <c>/hidden</c> and opened, saved or applied to, is the
/// evidence that suppression was wrong (invariant 11). Only suppressed rows of the latest Run count, and only
/// a positive reaction is regret — a shown job, an older Run's suppression, or no reaction at all are not. It
/// selects nothing about the Owner's CV. Requires Docker.
/// </summary>
public sealed class SuppressionRegretQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task A_suppressed_job_the_owner_opened_counts_as_regret()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var regretted = await SeedSuppressedAsync(database, companyId, runId);
        await SeedSuppressedAsync(database, companyId, runId); // suppressed, never acted on — not regret
        await SeedSignalAsync(database, regretted, SignalKind.Opened);

        var query = new SuppressionRegretQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.LatestRunRegretCountAsync()).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task A_shown_job_the_owner_opened_is_not_regret()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var shown = await SeedShownAsync(database, companyId, runId);
        await SeedSignalAsync(database, shown, SignalKind.Opened);

        var query = new SuppressionRegretQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.LatestRunRegretCountAsync()).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task A_negative_reaction_to_a_suppressed_job_is_not_regret()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var ignored = await SeedSuppressedAsync(database, companyId, runId);
        await SeedSignalAsync(database, ignored, SignalKind.Ignored);

        var query = new SuppressionRegretQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.LatestRunRegretCountAsync()).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task An_earlier_runs_regret_does_not_linger()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var oldRun = await SeedRunAsync(database, RunStart.AddDays(-1));
        var newRun = await SeedRunAsync(database, RunStart);

        var oldRegret = await SeedSuppressedAsync(database, companyId, oldRun);
        await SeedSignalAsync(database, oldRegret, SignalKind.Saved);
        // The latest Run suppressed a job too, but nobody acted on it.
        await SeedSuppressedAsync(database, companyId, newRun);

        var query = new SuppressionRegretQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.LatestRunRegretCountAsync()).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task A_job_acted_on_more_than_once_is_counted_once()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var regretted = await SeedSuppressedAsync(database, companyId, runId);
        await SeedSignalAsync(database, regretted, SignalKind.Opened);
        await SeedSignalAsync(database, regretted, SignalKind.Saved);

        var query = new SuppressionRegretQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.LatestRunRegretCountAsync()).ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task No_suppression_no_regret()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var shown = await SeedShownAsync(database, companyId, runId);
        await SeedSignalAsync(database, shown, SignalKind.Applied);

        var query = new SuppressionRegretQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.LatestRunRegretCountAsync()).ShouldBe(0);
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
        run.Abort("seeded", startedAt.AddMinutes(1), costBreach: false);
        ctx.Add(run);
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static async Task<Guid> SeedSuppressedAsync(TestDatabase database, Guid companyId, Guid runId)
    {
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, runId, 35m, suppressed: true, reason: "Below the bar");
        return jobId;
    }

    private static async Task<Guid> SeedShownAsync(TestDatabase database, Guid companyId, Guid runId)
    {
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, runId, 88m, suppressed: false, reason: null);
        return jobId;
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
        TestDatabase database, Guid jobId, Guid runId, decimal finalScore, bool suppressed, string? reason)
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

    private static async Task SeedSignalAsync(TestDatabase database, Guid jobId, SignalKind kind)
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Country] = ["DE"],
        });
        var applicationId = IsOutcome(kind) ? Guid.CreateVersion7() : (Guid?)null;
        var signal = Signal.Capture(
            Guid.CreateVersion7(), jobId, applicationId, kind, facts, FirstSeen.AddMinutes(kind.GetHashCode() % 1000), SignalWeights.Default);

        var repo = new SignalRepository(new NpgsqlConnectionFactory(database.ConnectionString));
        (await repo.TryCaptureAsync(signal)).ShouldBeTrue();
    }

    private static bool IsOutcome(SignalKind kind) =>
        kind is SignalKind.Applied or SignalKind.Interview or SignalKind.Offer or SignalKind.Rejected;
}
