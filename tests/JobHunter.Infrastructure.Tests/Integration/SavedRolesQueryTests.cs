using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Profiles;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Shouldly;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// F5 T11: the read side of <c>/saved</c> (contract §Commands, AC-12). A save is a <c>Saved</c>-kind row in
/// <c>signals</c> (F7 owns the table, F5 writes the action); this query joins those rows back to the job, its
/// company, its latest score and its current match so <c>/saved</c> renders the same card the digest did —
/// title, company · stage · location, salary, score and the ranking's own reasons (invariant 4). The
/// load-bearing properties: only <c>Saved</c> signals are returned (an Opened or Ignored is not a save);
/// rows come newest-first and are capped at the caller's limit; a superseded match does not supply the
/// reasons; and the salary and score are the job's own and its most recent score. Read-only — Dapper never
/// writes (architecture rule 4). Requires Docker.
/// </summary>
public sealed class SavedRolesQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SavedAt = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Saved_returns_a_saved_role_with_its_card_display_fields()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedMatchAsync(database, jobId, runId, profileId, cvVersionId, reasons: ["Strong platform fit."]);
        await SeedScoreAsync(database, jobId, runId, finalScore: 88m);
        await SeedSavedAsync(database, jobId, SavedAt);

        var query = new SavedRolesQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var saved = await query.SavedAsync(10);

        var role = saved.ShouldHaveSingleItem();
        role.JobId.ShouldBe(jobId);
        role.Title.ShouldBe("Staff SRE");
        role.Company.ShouldBe("Acme");
        role.Stage.ShouldBe("Series B");
        role.Countries.ShouldBe(["Germany"]);
        role.SalaryMin.ShouldBe(150_000);
        role.SalaryMax.ShouldBe(180_000);
        role.SalaryCurrency.ShouldBe("USD");
        role.Score.ShouldBe(88m);
        role.Reasons.ShouldBe(["Strong platform fit."]);
        role.SavedAt.ShouldBe(SavedAt);
    }

    [RequiresDockerFact]
    public async Task Saved_ignores_signals_that_are_not_saves()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedSignalAsync(database, jobId, SignalKind.Opened, SavedAt);
        await SeedSignalAsync(database, jobId, SignalKind.Ignored, SavedAt.AddMinutes(1));

        var query = new SavedRolesQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        // An open or an ignore is a reaction, not a save — /saved shows saves only.
        (await query.SavedAsync(10)).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task Saved_orders_newest_first_and_caps_at_the_limit()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var oldJob = await SeedJobAsync(database, companyId);
        var midJob = await SeedJobAsync(database, companyId);
        var newJob = await SeedJobAsync(database, companyId);
        await SeedSavedAsync(database, oldJob, SavedAt);
        await SeedSavedAsync(database, midJob, SavedAt.AddHours(1));
        await SeedSavedAsync(database, newJob, SavedAt.AddHours(2));

        var query = new SavedRolesQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var saved = await query.SavedAsync(2);

        // Newest first, and only the two most recent when the limit is two.
        saved.Select(r => r.JobId).ShouldBe([newJob, midJob]);
    }

    [RequiresDockerFact]
    public async Task Saved_does_not_take_reasons_from_a_superseded_match()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedMatchAsync(
            database, jobId, runId, profileId, cvVersionId, reasons: ["Stale reason."], current: false);
        await SeedScoreAsync(database, jobId, runId, finalScore: 70m);
        await SeedSavedAsync(database, jobId, SavedAt);

        var query = new SavedRolesQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var role = (await query.SavedAsync(10)).ShouldHaveSingleItem();

        role.Reasons.ShouldBeEmpty();
    }

    private static Task SeedSavedAsync(TestDatabase database, Guid jobId, DateTimeOffset at) =>
        SeedSignalAsync(database, jobId, SignalKind.Saved, at);

    private static async Task SeedSignalAsync(
        TestDatabase database, Guid jobId, SignalKind kind, DateTimeOffset at)
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.RemotePolicy] = ["Remote"],
        });
        var signal = new Signal(Guid.CreateVersion7(), jobId, applicationId: null, kind, weight: 1m, facts, at);
        var repo = new SignalRepository(new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.TryCaptureAsync(signal);
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

    private static async Task SeedScoreAsync(TestDatabase database, Guid jobId, Guid runId, decimal finalScore)
    {
        var fraction = finalScore / 100m;
        var components = new ScoreComponents(
            match: fraction, alignment: fraction, preference: fraction, freshness: fraction,
            confidenceMultiplier: 1.00m);
        var score = new Score(
            jobId, runId, finalScore, components, RankingWeights.Default, preferenceModelId: null,
            suppressed: false, suppressionReason: null, RunStart);

        var repo = new ScoreRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.UpsertAsync(score);
    }
}
