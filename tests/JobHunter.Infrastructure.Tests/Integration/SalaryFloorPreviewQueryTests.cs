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
/// F10 T08: the read side of <c>/floor</c>'s preview (catalogue §Profile). Before the floor is written, the
/// command states how many of today's jobs it would have affected — a below-floor role the Owner can weigh the
/// change against. "Affected" follows the same rule the ranking's <see cref="SuppressionEvaluator"/> applies:
/// the latest Run's shown (non-suppressed) roles whose <em>high-confidence, same-currency</em> estimated pay
/// sits wholly below the proposed floor (even the top of the band misses it). The load-bearing properties, each
/// asserted against a real database: a wholly-below high-confidence estimate counts; a low-confidence one does
/// not; a different-currency one does not (an FX comparison would be a lie); a job whose band top reaches the
/// floor does not; a suppressed job never counts; only the latest Run is considered. It selects
/// <strong>nothing about the Owner's CV</strong> (F4 invariant). Read-only — Dapper never writes. Requires Docker.
/// </summary>
public sealed class SalaryFloorPreviewQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task A_high_confidence_same_currency_estimate_wholly_below_the_floor_is_counted()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedEnrichmentAsync(database, runId, jobId, min: 60_000m, max: 90_000m, currency: "EUR", confidence: 0.90m);
        await SeedScoreAsync(database, jobId, runId, finalScore: 64m, suppressed: false);

        var query = new SalaryFloorPreviewQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var affected = await query.CountAffectedAsync(120_000m, "EUR");

        affected.ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task A_low_confidence_estimate_is_not_counted()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedEnrichmentAsync(database, runId, jobId, min: 60_000m, max: 90_000m, currency: "EUR", confidence: 0.50m);
        await SeedScoreAsync(database, jobId, runId, finalScore: 64m, suppressed: false);

        var query = new SalaryFloorPreviewQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        // A guess cannot condemn a role to below-floor — only a high-confidence estimate bites (O5).
        (await query.CountAffectedAsync(120_000m, "EUR")).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task A_different_currency_estimate_is_not_counted()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedEnrichmentAsync(database, runId, jobId, min: 60_000m, max: 90_000m, currency: "USD", confidence: 0.90m);
        await SeedScoreAsync(database, jobId, runId, finalScore: 64m, suppressed: false);

        var query = new SalaryFloorPreviewQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        // Never compare euros against dollars — a cross-currency verdict would be a lie.
        (await query.CountAffectedAsync(120_000m, "EUR")).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task An_estimate_whose_band_top_reaches_the_floor_is_not_counted()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedEnrichmentAsync(database, runId, jobId, min: 100_000m, max: 130_000m, currency: "EUR", confidence: 0.90m);
        await SeedScoreAsync(database, jobId, runId, finalScore: 64m, suppressed: false);

        var query = new SalaryFloorPreviewQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        // Only wholly-below counts: the top of the band reaches the floor, so the role might still clear it.
        (await query.CountAffectedAsync(120_000m, "EUR")).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task A_suppressed_job_never_counts()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedEnrichmentAsync(database, runId, jobId, min: 60_000m, max: 90_000m, currency: "EUR", confidence: 0.90m);
        await SeedScoreAsync(database, jobId, runId, finalScore: 30m, suppressed: true);

        var query = new SalaryFloorPreviewQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        // A suppressed role is already withheld — the floor change does not "affect" what the digest shows.
        (await query.CountAffectedAsync(120_000m, "EUR")).ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task Only_the_latest_run_is_considered()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var oldRun = await SeedRunAsync(database, RunStart.AddDays(-1));
        var newRun = await SeedRunAsync(database, RunStart);
        var oldJob = await SeedJobAsync(database, companyId);
        var newJob = await SeedJobAsync(database, companyId);
        await SeedEnrichmentAsync(database, oldRun, oldJob, min: 60_000m, max: 90_000m, currency: "EUR", confidence: 0.90m);
        await SeedEnrichmentAsync(database, newRun, newJob, min: 60_000m, max: 90_000m, currency: "EUR", confidence: 0.90m);
        await SeedScoreAsync(database, oldJob, oldRun, finalScore: 60m, suppressed: false);
        await SeedScoreAsync(database, newJob, newRun, finalScore: 55m, suppressed: false);

        var query = new SalaryFloorPreviewQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        // Yesterday's below-floor set does not linger — the preview is about today.
        (await query.CountAffectedAsync(120_000m, "EUR")).ShouldBe(1);
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var company = new Company(
            companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, RunStart);
        ctx.Add(company);
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
            firstSeenAt: FirstSeen, lastSeenAt: FirstSeen,
            salary: SalaryRange.TryCreate(150_000m, 180_000m, "USD", SalaryPeriod.Year).Value,
            status: JobStatus.Live));
        await ctx.SaveChangesAsync();
        return jobId;
    }

    private static async Task SeedEnrichmentAsync(
        TestDatabase database, Guid enrichRunId, Guid jobId, decimal min, decimal max, string currency, decimal confidence)
    {
        var estimate = SalaryEstimate.TryCreate(min, max, currency, SalaryPeriod.Year, confidence).Value;
        var enrichment = new Enrichment(
            Guid.CreateVersion7(), jobId, enrichRunId, estimate,
            isRemote: true, isContractorFriendly: false, TimezoneBand.EMEA,
            AiUsageLevel.None, AiSignals.None, CompanyStage.SeriesB, RoleFamily.Platform,
            technologies: [], reasons: ["Assessed."], "enrich-v1", RunStart);

        var repo = new EnrichmentRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.UpsertAsync(enrichment);
    }

    private static async Task SeedScoreAsync(
        TestDatabase database, Guid jobId, Guid runId, decimal finalScore, bool suppressed)
    {
        var fraction = finalScore / 100m;
        var components = new ScoreComponents(
            match: fraction, alignment: fraction, preference: fraction, freshness: fraction,
            confidenceMultiplier: 1.00m);
        var score = new Score(
            jobId, runId, finalScore, components, RankingWeights.Default, preferenceModelId: null,
            suppressed, suppressed ? "Salary below floor" : null, RunStart);

        var repo = new ScoreRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.UpsertAsync(score);
    }
}
