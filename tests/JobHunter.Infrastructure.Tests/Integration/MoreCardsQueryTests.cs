using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Profiles;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F10 T06: the read side of <c>/more</c> (catalogue §Digest and discovery). It returns the roles the latest
/// Run <em>showed but did not card</em> — non-suppressed <c>scores</c> whose job is not one of the digest's
/// top cards — paired with the score and the current match's reasons (invariant 4), so <c>/more</c> renders
/// them through the one shared card layout. The load-bearing properties, each asserted here against a real
/// database: a shown-but-uncarded role appears with its reasons and score; a suppressed job never does; a
/// carded (top-cards) job never does; only the latest Run's scores are considered; the order is the frozen
/// <c>final_score DESC</c> so paging mid-morning cannot reshuffle it (PRD §8); and <c>take</c> caps the page
/// while <c>TotalBelowTheCut</c> reports the full below-the-cut count. It selects <strong>nothing about the
/// Owner's CV</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant). Read-only
/// — Dapper never writes (architecture rule 4). Requires Docker.
/// </summary>
public sealed class MoreCardsQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Below_the_cut_returns_a_shown_uncarded_role_with_its_score_and_reasons()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedMatchAsync(database, jobId, runId, profileId, cvVersionId, reasons: ["Kafka named as core."]);
        await SeedScoreAsync(database, jobId, runId, finalScore: 64m, suppressed: false);

        var query = new MoreCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var page = await query.BelowTheCutAsync(take: 5);

        var card = page.Cards.ShouldHaveSingleItem();
        card.Display.JobId.ShouldBe(jobId);
        card.Display.Title.ShouldBe("Staff SRE");
        card.Display.Company.ShouldBe("Acme");
        card.Display.Stage.ShouldBe("Series B");
        card.Display.Countries.ShouldBe(["Germany"]);
        card.Score.ShouldBe(64m);
        card.Reasons.ShouldBe(["Kafka named as core."]);
        page.TotalBelowTheCut.ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task A_suppressed_job_never_appears()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, runId, finalScore: 30m, suppressed: true);

        var query = new MoreCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var page = await query.BelowTheCutAsync(take: 5);

        // A suppressed role is below the F4 floor — it is hidden, not "below the cut"; /hidden owns it.
        page.Cards.ShouldBeEmpty();
        page.TotalBelowTheCut.ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task A_carded_role_never_appears()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var carded = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, carded, runId, finalScore: 82m, suppressed: false);
        await SeedDigestWithCardAsync(database, runId, carded);

        var query = new MoreCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var page = await query.BelowTheCutAsync(take: 5);

        // A top card is above the cut, not below it — /more shows only what the digest did not already carry.
        page.Cards.ShouldBeEmpty();
        page.TotalBelowTheCut.ShouldBe(0);
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
        await SeedScoreAsync(database, oldJob, oldRun, finalScore: 60m, suppressed: false);
        await SeedScoreAsync(database, newJob, newRun, finalScore: 55m, suppressed: false);

        var query = new MoreCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var page = await query.BelowTheCutAsync(take: 5);

        // Yesterday's below-the-cut set does not linger — /more pages the latest Run only.
        page.Cards.ShouldHaveSingleItem().Display.JobId.ShouldBe(newJob);
        page.TotalBelowTheCut.ShouldBe(1);
    }

    [RequiresDockerFact]
    public async Task Ordering_is_frozen_best_first_and_take_caps_the_page_while_total_reports_the_full_count()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var low = await SeedJobAsync(database, companyId);
        var mid = await SeedJobAsync(database, companyId);
        var high = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, low, runId, finalScore: 45m, suppressed: false);
        await SeedScoreAsync(database, mid, runId, finalScore: 55m, suppressed: false);
        await SeedScoreAsync(database, high, runId, finalScore: 65m, suppressed: false);

        var query = new MoreCardsQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var page = await query.BelowTheCutAsync(take: 2);

        // Best score first, the page capped at two, and the total reporting all three below the cut.
        page.Cards.Select(c => c.Display.JobId).ShouldBe([high, mid]);
        page.TotalBelowTheCut.ShouldBe(3);
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        var company = new Company(
            companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, RunStart);
        ctx.Add(company);
        // Stage is set by F3, not at construction; set it through EF for the card's "· Series B" line.
        ctx.Entry(company).Property(nameof(Company.Stage)).CurrentValue = "Series B";
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
            firstSeenAt: FirstSeen, lastSeenAt: FirstSeen,
            salary: SalaryRange.TryCreate(150_000m, 180_000m, "USD", SalaryPeriod.Year).Value,
            status: JobStatus.Live));
        await ctx.SaveChangesAsync();
        return jobId;
    }

    private static async Task SeedMatchAsync(
        TestDatabase database, Guid jobId, Guid runId, Guid profileId, Guid cvVersionId,
        IReadOnlyList<string> reasons, bool current = true)
    {
        await using var ctx = database.CreateContext();
        var match = new Match(
            Guid.CreateVersion7(), jobId, runId, profileId, cvVersionId, matchScore: 75,
            InterviewProbability.Good, missingSkills: [], salaryExpectation: null,
            reasons: reasons, promptVersion: "match-v1", RunStart);
        if (!current)
        {
            match.MarkNotCurrent();
        }

        ctx.Add(match);
        await ctx.SaveChangesAsync();
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

    private static async Task SeedDigestWithCardAsync(TestDatabase database, Guid runId, Guid jobId)
    {
        var digestId = Guid.CreateVersion7();
        var card = new DigestCard(
            Guid.CreateVersion7(), digestId, jobId, runId, rank: 1, score: 82m,
            reasons: ["Top of the day."], applyUrlVerified: true, groupedJobIds: null);
        var digest = new Digest(
            digestId, runId, DigestMode.Full, totalNewJobs: 1, strongMatches: 1, avgSalaryUsd: 120000m,
            suppressedCount: 0, suppressionBreakdown: [], carriedOverCount: 0, companiesChecked: 0,
            analysedCount: 0, degradedSources: [], narrative: "One strong lead.", NarrativeSource.Model,
            promptVersion: "digest-v1", cards: [card], generatedAt: RunStart, restoredCount: 0,
            learningEnabled: true);

        var repo = new DigestRepository(database.CreateContext());
        repo.Add(digest);
        await repo.SaveChangesAsync();
    }
}
