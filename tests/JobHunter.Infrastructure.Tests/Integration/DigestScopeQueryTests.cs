using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Profiles;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T03: the read side of a Run's digest scope (F5 SAD §6.1). It returns one row per <c>scores</c> row in the
/// Run — <em>shown and suppressed</em> — joined by a <c>LEFT JOIN LATERAL</c> to the job's current match for
/// the reasons the card explains itself with and the salary the header averages. The properties that carry the
/// stage: every score comes back, because the suppression breakdown is built from the suppressed ones
/// (invariant 11, AC-07) — dropping them here would make the footer a lie; the rows are ordered by
/// <c>final_score DESC, job_id</c> so card selection is deterministic (QG-3); only a USD salary expectation is
/// surfaced, a non-USD one left null rather than converted; and a scored-but-unmatched job (pre-match
/// suppressed, never judged) still returns, with no reasons and no salary. Requires Docker.
/// </summary>
public sealed class DigestScopeQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Candidates_returns_a_shown_score_with_its_current_match_reasons_and_usd_salary()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedMatchAsync(
            database, jobId, runId, profileId, cvVersionId,
            reasons: ["Strong platform fit.", "Remote EMEA."],
            salary: SalaryExpectation.TryCreate(120_000m, 160_000m, "USD").Value);
        await SeedScoreAsync(database, jobId, runId, finalScore: 82m, suppressed: false);

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);

        var candidate = candidates.ShouldHaveSingleItem();
        candidate.JobId.ShouldBe(jobId);
        candidate.FinalScore.ShouldBe(82m);
        candidate.Suppressed.ShouldBeFalse();
        candidate.SuppressionReason.ShouldBeNull();
        candidate.Reasons.ShouldBe(["Strong platform fit.", "Remote EMEA."]);
        // The midpoint of the USD band, not a converted figure.
        candidate.SalaryUsd.ShouldBe(140_000m);
    }

    [RequiresDockerFact]
    public async Task Candidates_projects_the_company_and_normalised_title_the_grouper_keys_on()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, runId, finalScore: 80m, suppressed: false);

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);

        // The near-duplicate grouper collapses on (company, normalised title) at assembly (F5-T13): the query
        // must surface both, or every candidate would look unique and never group.
        var candidate = candidates.ShouldHaveSingleItem();
        candidate.CompanyId.ShouldBe(companyId);
        candidate.NormalisedTitle.ShouldBe("staff sre");
    }

    [RequiresDockerFact]
    public async Task Candidates_returns_a_suppressed_score_so_the_breakdown_reconciles()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedMatchAsync(database, jobId, runId, profileId, cvVersionId, reasons: ["Below the bar."], salary: null);
        await SeedScoreAsync(
            database, jobId, runId, finalScore: 30m, suppressed: true,
            suppressionReason: "Below presentation threshold");

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);

        // Invariant 11: the suppressed row is not filtered out — it is what the footer counts.
        var candidate = candidates.ShouldHaveSingleItem();
        candidate.Suppressed.ShouldBeTrue();
        candidate.SuppressionReason.ShouldBe("Below presentation threshold");
    }

    [RequiresDockerFact]
    public async Task Candidates_leaves_a_non_usd_salary_null_rather_than_converting_it()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedMatchAsync(
            database, jobId, runId, profileId, cvVersionId, reasons: ["A fit."],
            salary: SalaryExpectation.TryCreate(90_000m, 110_000m, "EUR").Value);
        await SeedScoreAsync(database, jobId, runId, finalScore: 75m, suppressed: false);

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);

        // A fabricated FX rate is worse than an absent average, so a non-USD band is left null.
        candidates.ShouldHaveSingleItem().SalaryUsd.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task Candidates_returns_a_scored_but_unmatched_job_with_no_reasons_and_no_salary()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var jobId = await SeedJobAsync(database, companyId);
        // A pre-match-suppressed job is scored, suppressed and reasoned, but never judged by the model.
        await SeedScoreAsync(
            database, jobId, runId, finalScore: 10m, suppressed: true,
            suppressionReason: "Filtered before the deep tier");

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);

        var candidate = candidates.ShouldHaveSingleItem();
        candidate.Reasons.ShouldBeEmpty();
        candidate.SalaryUsd.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task Candidates_ignores_a_superseded_match_when_joining_reasons()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId);
        // The only match for this job was superseded by a CV re-upload: it must not supply the card's reasons.
        await SeedMatchAsync(
            database, jobId, runId, profileId, cvVersionId, reasons: ["Stale reason."], salary: null,
            current: false);
        await SeedScoreAsync(database, jobId, runId, finalScore: 80m, suppressed: false);

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);

        candidates.ShouldHaveSingleItem().Reasons.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task Candidates_are_ordered_by_final_score_descending_then_job_id()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var low = await SeedJobAsync(database, companyId);
        var high = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, low, runId, finalScore: 40m, suppressed: false);
        await SeedScoreAsync(database, high, runId, finalScore: 90m, suppressed: false);

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(runId);

        // Best first, so the assembler's Take(cap) is deterministic (QG-3).
        candidates.Select(c => c.JobId).ShouldBe([high, low]);
    }

    [RequiresDockerFact]
    public async Task Candidates_excludes_scores_of_another_run()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var otherRun = await SeedRunAsync(database);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, otherRun, finalScore: 90m, suppressed: false);

        var query = new DigestScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var candidates = await query.CandidatesAsync(Guid.CreateVersion7());

        candidates.ShouldBeEmpty();
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
        // Retire the run immediately so the single-active-run partial index allows a second one in the same test.
        run.Abort("seeded", RunStart.AddMinutes(1), costBreach: false);
        ctx.Add(run);
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static async Task<Guid> SeedProfileAsync(TestDatabase database)
    {
        var profileId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Profile(profileId, isActive: true, "Owner", salaryFloor: null, salaryFloorCurrency: null,
            TimezoneBand.EMEA, ["Portugal"], [EmploymentType.FullTime], RunStart));
        await ctx.SaveChangesAsync();
        return profileId;
    }

    private static async Task<Guid> SeedCvVersionAsync(TestDatabase database, Guid profileId)
    {
        var cvId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new CvVersion(cvId, profileId, 1, true, "cv.pdf", "application/pdf", 2048,
            new string('a', 64), "extracted text", RunStart, RunStart));
        await ctx.SaveChangesAsync();
        return cvId;
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

    private static async Task SeedMatchAsync(
        TestDatabase database, Guid jobId, Guid runId, Guid profileId, Guid cvVersionId,
        IReadOnlyList<string> reasons, SalaryExpectation? salary, bool current = true)
    {
        await using var ctx = database.CreateContext();
        var match = new Match(
            Guid.CreateVersion7(), jobId, runId, profileId, cvVersionId, matchScore: 75,
            InterviewProbability.Good, missingSkills: [], salaryExpectation: salary,
            reasons: reasons, promptVersion: "match-v1", RunStart);
        if (!current)
        {
            match.MarkNotCurrent();
        }

        ctx.Add(match);
        await ctx.SaveChangesAsync();
    }

    private static async Task SeedScoreAsync(
        TestDatabase database, Guid jobId, Guid runId, decimal finalScore, bool suppressed,
        string? suppressionReason = null)
    {
        // Build components that reconcile to the requested total: with the weights summing to 1, setting every
        // component to finalScore/100 makes 100 × Σ(w·comp) × 1 × 1 = finalScore, and each fraction stays in [0,1].
        var fraction = finalScore / 100m;
        var components = new ScoreComponents(
            match: fraction, alignment: fraction, preference: fraction, freshness: fraction,
            confidenceMultiplier: 1.00m);
        var score = new Score(
            jobId, runId, finalScore, components, RankingWeights.Default, preferenceModelId: null,
            suppressed, suppressionReason, RunStart);

        var repo = new ScoreRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.UpsertAsync(score);
    }
}
