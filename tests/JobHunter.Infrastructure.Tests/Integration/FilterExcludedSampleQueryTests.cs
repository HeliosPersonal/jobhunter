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
/// F4 T21 (ADR-F4-0003): the read side of the regret sampler. It returns a sample of the jobs the pre-match
/// filter excluded from the latest Run's deep tier — a <c>suppressed</c> score with <strong>no match</strong>
/// for that job and Run — reconstructed as the <see cref="MatchJobContent"/> a real match would have judged.
/// A post-ranking suppression (a suppressed score that <em>does</em> have a match) is never sampled, which is
/// what makes the query measure the filter and only the filter. Only the latest Run's exclusions are returned,
/// deterministically ordered and capped. It selects nothing about the Owner's CV. Requires Docker.
/// </summary>
public sealed class FilterExcludedSampleQueryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task It_samples_a_filter_excluded_job_reconstructed_as_match_content()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, jobId, runId, suppressed: true, reason: "Employment type not sought");

        var query = new FilterExcludedSampleQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var sample = await query.SampleAsync(limit: 20);

        var job = sample.ShouldHaveSingleItem();
        job.JobId.ShouldBe(jobId);
        job.Title.ShouldBe("Staff SRE");
        job.CompanyName.ShouldBe("Acme");
        job.Description.ShouldBe("We keep the lights on.");
        job.EmploymentType.ShouldBe(EmploymentType.FullTime.ToString());
        // No enrichment was seeded, so the enrichment lines are omitted rather than filled (AC-09).
        job.Enrichment.ShouldBeNull();
    }

    [RequiresDockerFact]
    public async Task A_post_ranking_suppression_that_has_a_match_is_never_sampled()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var (profileId, cvVersionId) = await SeedProfileAsync(database);
        var runId = await SeedRunAsync(database, RunStart);

        var filtered = await SeedJobAsync(database, companyId);
        var scoredButHidden = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, filtered, runId, suppressed: true, reason: "Salary below floor (pre-match)");
        await SeedScoreAsync(database, scoredButHidden, runId, suppressed: true, reason: "final_score < 40");
        // The post-ranking suppression reached the deep tier: it has a match row, so it is NOT a filter exclusion.
        await SeedMatchAsync(database, scoredButHidden, runId, profileId, cvVersionId);

        var query = new FilterExcludedSampleQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var sample = await query.SampleAsync(limit: 20);

        sample.ShouldHaveSingleItem().JobId.ShouldBe(filtered);
    }

    [RequiresDockerFact]
    public async Task A_shown_job_is_never_sampled()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var shown = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, shown, runId, suppressed: false, reason: null);

        var query = new FilterExcludedSampleQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.SampleAsync(limit: 20)).ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task Only_the_latest_run_is_sampled_so_an_older_exclusion_does_not_distort_regret()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var oldRun = await SeedRunAsync(database, RunStart.AddDays(-1));
        var newRun = await SeedRunAsync(database, RunStart);
        var oldJob = await SeedJobAsync(database, companyId);
        var newJob = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, oldJob, oldRun, suppressed: true, reason: "Old exclusion");
        await SeedScoreAsync(database, newJob, newRun, suppressed: true, reason: "New exclusion");

        var query = new FilterExcludedSampleQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var sample = await query.SampleAsync(limit: 20);

        sample.ShouldHaveSingleItem().JobId.ShouldBe(newJob);
    }

    [RequiresDockerFact]
    public async Task The_sample_is_capped_at_the_caller_limit()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var first = await SeedJobAsync(database, companyId);
        var second = await SeedJobAsync(database, companyId);
        await SeedScoreAsync(database, first, runId, suppressed: true, reason: "One");
        await SeedScoreAsync(database, second, runId, suppressed: true, reason: "Two");

        var query = new FilterExcludedSampleQuery(new NpgsqlConnectionFactory(database.ConnectionString));

        (await query.SampleAsync(limit: 1)).Count.ShouldBe(1);
        (await query.SampleAsync(limit: 20)).Count.ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task The_jobs_latest_enrichment_is_attached_when_present()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;
        var companyId = await SeedCompanyAsync(database);
        var runId = await SeedRunAsync(database, RunStart);
        var jobId = await SeedJobAsync(database, companyId);
        await SeedEnrichmentAsync(database, jobId, runId);
        await SeedScoreAsync(database, jobId, runId, suppressed: true, reason: "Timezone incompatible and not remote");

        var query = new FilterExcludedSampleQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        var job = (await query.SampleAsync(limit: 20)).ShouldHaveSingleItem();

        job.Enrichment.ShouldNotBeNull();
        job.Enrichment!.TimezoneBand.ShouldBe(TimezoneBand.EMEA);
        job.Enrichment.IsRemote.ShouldBeFalse();
    }

    private static async Task<Guid> SeedCompanyAsync(TestDatabase database)
    {
        var companyId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, RunStart));
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static async Task<(Guid ProfileId, Guid CvVersionId)> SeedProfileAsync(TestDatabase database)
    {
        var profileId = Guid.CreateVersion7();
        var cvVersionId = Guid.CreateVersion7();
        await using var ctx = database.CreateContext();
        ctx.Add(new Profile(
            profileId, isActive: true, displayName: "Owner", salaryFloor: null, salaryFloorCurrency: null,
            TimezoneBand.EMEA, preferredCountries: ["Germany"], employmentTypes: [EmploymentType.FullTime],
            updatedAt: RunStart));
        ctx.Add(new CvVersion(
            cvVersionId, profileId, version: 1, isActive: true, fileName: "cv.pdf", mediaType: "application/pdf",
            sizeBytes: 1024, contentHash: ContentHash.Compute("cv").Value, extractedText: "the cv text",
            uploadedAt: RunStart, activatedAt: RunStart));
        await ctx.SaveChangesAsync();
        return (profileId, cvVersionId);
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

    private static async Task SeedEnrichmentAsync(TestDatabase database, Guid jobId, Guid runId)
    {
        var repo = new EnrichmentRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        var enrichment = new Enrichment(
            Guid.CreateVersion7(), jobId, runId, salary: null, isRemote: false, isContractorFriendly: false,
            TimezoneBand.EMEA, AiUsageLevel.None,
            new AiSignals(buildsAiProduct: false, buildsAiInfra: false, usesAiTooling: false, isResearch: false),
            CompanyStage.SeriesA, RoleFamily.Platform, technologies: ["Go", "Kubernetes"],
            reasons: ["onsite-only"], promptVersion: "enrich-v1", createdAt: RunStart);
        await repo.UpsertAsync(enrichment);
    }

    private static async Task SeedScoreAsync(
        TestDatabase database, Guid jobId, Guid runId, bool suppressed, string? reason)
    {
        // A pre-match exclusion is persisted with zero components and a zero final score (MatchingSubmitHandler
        // RecordExclusionAsync); the discriminator this query uses is "suppressed AND no match", not the score.
        var components = new ScoreComponents(
            match: 0m, alignment: 0m, preference: 0m, freshness: 0m, confidenceMultiplier: 1.00m);
        var score = new Score(
            jobId, runId, finalScore: 0m, components, RankingWeights.Default, preferenceModelId: null,
            suppressed, reason, RunStart);

        var repo = new ScoreRepository(
            database.CreateContext(), new NpgsqlConnectionFactory(database.ConnectionString));
        await repo.UpsertAsync(score);
    }

    private static async Task SeedMatchAsync(
        TestDatabase database, Guid jobId, Guid runId, Guid profileId, Guid cvVersionId)
    {
        await using var ctx = database.CreateContext();
        ctx.Add(new Match(
            Guid.CreateVersion7(), jobId, runId, profileId, cvVersionId, matchScore: 55,
            InterviewProbability.Moderate, missingSkills: [], salaryExpectation: null,
            reasons: ["a real judgement"], promptVersion: "match-v1", createdAt: RunStart));
        await ctx.SaveChangesAsync();
    }
}
