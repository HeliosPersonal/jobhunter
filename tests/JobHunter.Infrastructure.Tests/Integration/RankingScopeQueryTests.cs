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
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T08: the read side of a Run's ranking scope (F4 SAD §6.2). It returns one row per <em>current</em> match in the
/// Run — the model's fit judgement joined to the job's first-seen timestamp and, by a <c>LEFT JOIN LATERAL</c>, the
/// job's latest enrichment. The properties that carry the stage: a superseded (not-current) match is excluded so a
/// CV re-upload mid-Run is not ranked (AC-08); a matched-but-unenriched job comes back with <c>HasEnrichment</c>
/// false and a null estimate rather than being dropped, so it is still ranked at a discounted confidence (AC-09);
/// and matches of another Run are never in scope. Requires Docker.
/// </summary>
public sealed class RankingScopeQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task InScope_returns_a_current_match_with_first_seen_and_the_latest_enrichment()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId, JobStatus.Live);
        await SeedEnrichmentAsync(database, jobId, runId);
        await SeedMatchAsync(database, jobId, runId, profileId, cvVersionId, matchScore: 82);

        var query = new RankingScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(runId);

        var job = scope.ShouldHaveSingleItem();
        job.JobId.ShouldBe(jobId);
        job.MatchScore.ShouldBe(82);
        job.FirstSeenAt.ShouldBe(FirstSeen);
        job.HasEnrichment.ShouldBeTrue();
        job.EstimatedSalary.ShouldNotBeNull();
        job.EstimatedSalary.Min.ShouldBe(120_000m);
        job.EstimatedSalary.Currency.ShouldBe("USD");
    }

    [RequiresDockerFact]
    public async Task InScope_returns_an_unenriched_match_with_no_enrichment_rather_than_dropping_it()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId, JobStatus.Live);
        await SeedMatchAsync(database, jobId, runId, profileId, cvVersionId, matchScore: 60);

        var query = new RankingScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(runId);

        // AC-09: still ranked, at a discounted confidence, never lost for lacking an enrichment.
        var job = scope.ShouldHaveSingleItem();
        job.JobId.ShouldBe(jobId);
        job.HasEnrichment.ShouldBeFalse();
        job.EstimatedSalary.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task InScope_excludes_a_superseded_not_current_match()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId, JobStatus.Live);
        await SeedMatchAsync(database, jobId, runId, profileId, cvVersionId, matchScore: 70, current: false);

        var query = new RankingScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(runId);

        scope.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task InScope_excludes_matches_of_another_run()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var otherRun = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobId = await SeedJobAsync(database, companyId, JobStatus.Live);
        await SeedMatchAsync(database, jobId, otherRun, profileId, cvVersionId, matchScore: 90);

        var query = new RankingScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(Guid.CreateVersion7());

        scope.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task InScope_orders_by_job_id_so_a_rerun_sees_the_same_order()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database);
        var profileId = await SeedProfileAsync(database);
        var cvVersionId = await SeedCvVersionAsync(database, profileId);
        var jobA = await SeedJobAsync(database, companyId, JobStatus.Live);
        var jobB = await SeedJobAsync(database, companyId, JobStatus.Live);
        await SeedMatchAsync(database, jobA, runId, profileId, cvVersionId, matchScore: 50);
        await SeedMatchAsync(database, jobB, runId, profileId, cvVersionId, matchScore: 60);

        var query = new RankingScopeQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var scope = await query.InScopeAsync(runId);

        scope.Select(j => j.JobId).ShouldBe(new[] { jobA, jobB }.OrderBy(id => id).ToList());
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

    private static async Task<Guid> SeedJobAsync(TestDatabase database, Guid companyId, JobStatus status)
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
            firstSeenAt: FirstSeen, lastSeenAt: FirstSeen, salary: null, status: status));
        await ctx.SaveChangesAsync();
        return jobId;
    }

    private static async Task SeedEnrichmentAsync(TestDatabase database, Guid jobId, Guid runId)
    {
        var repo = new EnrichmentRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        var enrichment = new Enrichment(
            Guid.CreateVersion7(), jobId, runId,
            SalaryEstimate.TryCreate(120_000m, 160_000m, "USD", SalaryPeriod.Year, 0.8m).Value,
            isRemote: true, isContractorFriendly: false, TimezoneBand.EMEA, AiUsageLevel.Medium,
            new AiSignals(buildsAiProduct: false, buildsAiInfra: true, usesAiTooling: true, isResearch: false),
            CompanyStage.SeriesB, RoleFamily.Platform, technologies: ["C#", ".NET"],
            reasons: ["Salary band inferred from peers."],
            promptVersion: "enrich-v1", RunStart);
        await repo.UpsertAsync(enrichment);
    }

    private static async Task SeedMatchAsync(
        TestDatabase database, Guid jobId, Guid runId, Guid profileId, Guid cvVersionId, int matchScore,
        bool current = true)
    {
        await using var ctx = database.CreateContext();
        var match = new Match(
            Guid.CreateVersion7(), jobId, runId, profileId, cvVersionId, matchScore,
            InterviewProbability.Good, missingSkills: [], salaryExpectation: null,
            reasons: ["Strong platform fit."], promptVersion: "match-v1", RunStart);
        if (!current)
        {
            match.MarkNotCurrent();
        }

        ctx.Add(match);
        await ctx.SaveChangesAsync();
    }
}
