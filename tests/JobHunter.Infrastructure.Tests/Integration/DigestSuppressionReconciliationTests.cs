using JobHunter.Application.Reporting;
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
using Npgsql;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F7 T07 (AC-07, invariant 11): the digest's suppression breakdown reconciles, end to end, to the suppressed
/// <c>scores</c> rows in the database — per reason, not just in total. This is the property that makes
/// [[DECISION-LOG|D7]] real across the persistence boundary: a hidden job is filtered for a <em>stated</em>
/// reason the Owner can count, never silently lost to a bug. Where <see cref="SuppressionSummarizerTests"/>
/// prove the grouping in isolation, this drives it through the real <see cref="DigestScopeQuery"/> read against
/// Postgres and compares the result to a ground-truth SQL aggregate of the <c>scores</c> table — so a query
/// that dropped a suppressed row, or a summarizer that miscounted a reason, would be caught here. Requires
/// Docker.
/// </summary>
public sealed class DigestSuppressionReconciliationTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task The_digest_suppression_breakdown_reconciles_to_the_suppressed_score_rows_per_reason()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);

        // A realistic Run: two shown scores that never enter the breakdown, and six suppressed across three
        // distinct reasons with differing counts, so a per-reason miscount cannot hide behind a right total.
        await SeedScoreAsync(database, companyId, runId, 82m, suppressed: false);
        await SeedScoreAsync(database, companyId, runId, 78m, suppressed: false);
        await SeedScoreAsync(database, companyId, runId, 30m, suppressed: true, reason: "Below presentation threshold");
        await SeedScoreAsync(database, companyId, runId, 28m, suppressed: true, reason: "Below presentation threshold");
        await SeedScoreAsync(database, companyId, runId, 26m, suppressed: true, reason: "Below presentation threshold");
        await SeedScoreAsync(database, companyId, runId, 24m, suppressed: true, reason: "Below your salary floor");
        await SeedScoreAsync(database, companyId, runId, 22m, suppressed: true, reason: "Below your salary floor");
        await SeedScoreAsync(database, companyId, runId, 12m, suppressed: true, reason: "Not a target role family: MlResearch");

        // The production read + grouping the assembler uses: every score comes back from the scope query, and
        // the summarizer folds the suppressed ones into the footer's breakdown (invariant 11, AC-07).
        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);
        var breakdown = SuppressionSummarizer.Summarize(candidates);

        // Ground truth straight from the table: the suppressed rows this Run actually wrote, counted per reason.
        var truth = await SuppressedCountsByReasonAsync(database, runId);

        breakdown
            .ToDictionary(t => t.Reason, t => t.Count)
            .ShouldBe(truth);
        // And the whole reconciles: the digest's suppressed count is exactly the suppressed rows in the table.
        breakdown.Sum(t => t.Count).ShouldBe(truth.Values.Sum());
        candidates.Count(c => c.Suppressed).ShouldBe(truth.Values.Sum());
    }

    [RequiresDockerFact]
    public async Task A_suppressed_row_with_no_reason_still_reconciles_under_the_unspecified_bucket()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);

        // The domain Score aggregate refuses to construct a suppressed row without a reason (invariant 11), so
        // the only way one reaches the table is a raw write that bypasses it. If that ever happens, losing the
        // row from the count is the exact failure the breakdown exists to prevent: the read must still surface
        // it, and the summarizer bucket it under "Unspecified" rather than drop it.
        await SeedScoreAsync(database, companyId, runId, 20m, suppressed: true, reason: "Below the bar");
        await RawInsertSuppressedScoreWithNoReasonAsync(database, companyId, runId, 18m);

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);
        var breakdown = SuppressionSummarizer.Summarize(candidates);

        // Two suppressed rows in, two accounted for out — one under its reason, one under "Unspecified".
        breakdown.Sum(t => t.Count).ShouldBe(2);
        breakdown.ShouldContain(t => t.Reason == SuppressionSummarizer.UnspecifiedReason && t.Count == 1);
    }

    // Writes a suppressed score with a NULL suppression_reason straight to the table, bypassing the Score
    // aggregate's invariant-11 guard — the only way such a row could exist — so the read path can be proven to
    // count it rather than silently lose it.
    private static async Task RawInsertSuppressedScoreWithNoReasonAsync(
        TestDatabase database, Guid companyId, Guid runId, decimal finalScore)
    {
        var jobId = await SeedJobAsync(database, companyId);
        var fraction = finalScore / 100m;

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO scores (job_id, run_id, final_score, match_component, alignment_component, " +
            "preference_component, freshness_component, confidence_multiplier, anti_goal_multiplier, " +
            "preference_model_id, suppressed, suppression_reason, computed_at) " +
            "VALUES (@job, @run, @final, @c, @c, @c, @c, 1.00, 1.00, NULL, true, NULL, @at)";
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("final", finalScore);
        command.Parameters.AddWithValue("c", fraction);
        command.Parameters.AddWithValue("at", RunStart);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyDictionary<string, int>> SuppressedCountsByReasonAsync(
        TestDatabase database, Guid runId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT suppression_reason, COUNT(*) FROM scores WHERE run_id = @run AND suppressed GROUP BY suppression_reason";
        command.Parameters.AddWithValue("run", runId);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = (int)reader.GetInt64(1);
        }

        return counts;
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, RunStart));
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static async Task<Guid> SeedRunAsync(TestDatabase database)
    {
        var runId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var run = new Run(runId, RunStart.AddDays(-1), RunStart, ceilingUsd: 5m, RunStart);
        run.Abort("seeded", RunStart.AddMinutes(1), costBreach: false);
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
        TestDatabase database, Guid companyId, Guid runId, decimal finalScore, bool suppressed,
        string? reason = null)
    {
        var jobId = await SeedJobAsync(database, companyId);

        // Components that reconcile to the requested total (weights sum to 1): every component finalScore/100.
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
