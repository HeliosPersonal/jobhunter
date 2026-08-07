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
/// F7 T09 done-when 4 (AC-08): <c>precision@10</c> queryable from recorded data, before and after a learned
/// model became active. For each Run it takes the shown (never suppressed) top-ten scores and asks how many
/// the Owner then engaged with, bucketing on whether the Run's scores carried a <c>preference_model_id</c> —
/// so the "before" and "after" halves of the series are directly comparable and the question "was any of this
/// worth building?" is answerable from stored rows alone. A suppressed job never counts, only the top ten
/// count, and a positive reaction is what makes a hit. It selects nothing about the Owner's CV. Requires Docker.
/// </summary>
public sealed class PrecisionAtTenQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task A_before_activation_run_reports_the_hit_rate_of_its_shown_top_ten()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        // Four shown jobs, two of them opened: precision 2/4 = 0.5. No model active yet (null preference_model_id).
        var opened1 = await SeedShownScoreAsync(database, companyId, runId, 90m, modelId: null);
        var opened2 = await SeedShownScoreAsync(database, companyId, runId, 85m, modelId: null);
        await SeedShownScoreAsync(database, companyId, runId, 80m, modelId: null);
        await SeedShownScoreAsync(database, companyId, runId, 75m, modelId: null);
        await SeedSignalAsync(database, opened1, SignalKind.Opened);
        await SeedSignalAsync(database, opened2, SignalKind.Saved);

        var query = new PrecisionAtTenQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var point = (await query.SeriesAsync()).ShouldHaveSingleItem();

        point.RunId.ShouldBe(runId);
        point.AfterActivation.ShouldBeFalse();
        point.Considered.ShouldBe(4);
        point.Hits.ShouldBe(2);
        point.Precision.ShouldBe(0.5m);
    }

    [RequiresDockerFact]
    public async Task A_run_scored_with_a_model_is_bucketed_after_activation()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var model = Guid.CreateVersion7();

        var opened = await SeedShownScoreAsync(database, companyId, runId, 90m, modelId: model);
        await SeedShownScoreAsync(database, companyId, runId, 80m, modelId: model);
        await SeedSignalAsync(database, opened, SignalKind.Opened);

        var query = new PrecisionAtTenQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var point = (await query.SeriesAsync()).ShouldHaveSingleItem();

        point.AfterActivation.ShouldBeTrue();
        point.Considered.ShouldBe(2);
        point.Hits.ShouldBe(1);
        point.Precision.ShouldBe(0.5m);
    }

    [RequiresDockerFact]
    public async Task A_suppressed_job_is_never_considered_even_if_it_was_engaged_with()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var shown = await SeedShownScoreAsync(database, companyId, runId, 90m, modelId: null);
        var hidden = await SeedSuppressedScoreAsync(database, companyId, runId, 40m);
        await SeedSignalAsync(database, shown, SignalKind.Opened);
        // The suppressed job was retrieved and opened, but precision@10 only measures what was shown.
        await SeedSignalAsync(database, hidden, SignalKind.Opened);

        var query = new PrecisionAtTenQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var point = (await query.SeriesAsync()).ShouldHaveSingleItem();

        point.Considered.ShouldBe(1);
        point.Hits.ShouldBe(1);
        point.Precision.ShouldBe(1.0m);
    }

    [RequiresDockerFact]
    public async Task Only_the_top_ten_shown_jobs_are_considered()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        // Twelve shown jobs, scores 100 down to 89. The eleventh and twelfth (91, 89) are opened but fall below
        // the top ten, so they must not count as hits and must not inflate the denominator past ten.
        for (var i = 0; i < 12; i++)
        {
            var jobId = await SeedShownScoreAsync(database, companyId, runId, 100m - i, modelId: null);
            if (i >= 10)
            {
                await SeedSignalAsync(database, jobId, SignalKind.Opened);
            }
        }

        var query = new PrecisionAtTenQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var point = (await query.SeriesAsync()).ShouldHaveSingleItem();

        point.Considered.ShouldBe(10);
        point.Hits.ShouldBe(0);
        point.Precision.ShouldBe(0m);
    }

    [RequiresDockerFact]
    public async Task A_negative_reaction_is_not_a_hit()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var ignored = await SeedShownScoreAsync(database, companyId, runId, 90m, modelId: null);
        await SeedSignalAsync(database, ignored, SignalKind.Ignored);

        var query = new PrecisionAtTenQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var point = (await query.SeriesAsync()).ShouldHaveSingleItem();

        point.Considered.ShouldBe(1);
        point.Hits.ShouldBe(0);
        point.Precision.ShouldBe(0m);
    }

    [RequiresDockerFact]
    public async Task The_series_returns_a_point_per_run_oldest_first()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var early = await SeedRunAsync(database, RunStart.AddDays(-1));
        var late = await SeedRunAsync(database, RunStart);

        await SeedShownScoreAsync(database, companyId, early, 90m, modelId: null);
        await SeedShownScoreAsync(database, companyId, late, 90m, modelId: Guid.CreateVersion7());

        var query = new PrecisionAtTenQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var series = await query.SeriesAsync();

        series.Select(p => p.RunId).ShouldBe([early, late]);
        series[0].AfterActivation.ShouldBeFalse();
        series[1].AfterActivation.ShouldBeTrue();
    }

    [RequiresDockerFact]
    public async Task A_run_that_showed_nothing_produces_no_point()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        await SeedSuppressedScoreAsync(database, companyId, runId, 40m);

        var query = new PrecisionAtTenQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.SeriesAsync()).ShouldBeEmpty();
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

    private static async Task<Guid> SeedShownScoreAsync(
        TestDatabase database, Guid companyId, Guid runId, decimal finalScore, Guid? modelId)
    {
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, runId, finalScore, suppressed: false, reason: null, modelId);
        return jobId;
    }

    private static async Task<Guid> SeedSuppressedScoreAsync(
        TestDatabase database, Guid companyId, Guid runId, decimal finalScore)
    {
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, runId, finalScore, suppressed: true, reason: "Below the bar", modelId: null);
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
        TestDatabase database, Guid jobId, Guid runId, decimal finalScore, bool suppressed, string? reason, Guid? modelId)
    {
        var fraction = finalScore / 100m;
        var components = new ScoreComponents(
            match: fraction, alignment: fraction, preference: fraction, freshness: fraction,
            confidenceMultiplier: 1.00m);
        var score = new Score(
            jobId, runId, finalScore, components, RankingWeights.Default, modelId, suppressed, reason, RunStart);

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
            Guid.CreateVersion7(), jobId, applicationId, kind, facts, FirstSeen, SignalWeights.Default);

        var repo = new SignalRepository(new NpgsqlConnectionFactory(database.ConnectionString));
        (await repo.TryCaptureAsync(signal)).ShouldBeTrue();
    }

    private static bool IsOutcome(SignalKind kind) =>
        kind is SignalKind.Applied or SignalKind.Interview or SignalKind.Offer or SignalKind.Rejected;
}
