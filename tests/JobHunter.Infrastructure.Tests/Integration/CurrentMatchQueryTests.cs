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
/// T12 / ADR-F4-0003: the read side of the pre-match filter's lifecycle rule. <see cref="CurrentMatchQuery"/>
/// answers, for a set of candidate jobs, which already carry a <em>current</em> match against a given CV
/// version — the one fact the submit handler resolves so the pure filter never names the <c>matches</c> table.
/// A match whose CV version was superseded (its <c>is_current</c> flag cleared by the re-staling sweep) must not
/// count, so the job re-opens for matching (AC-08). An empty id set is answered without a round trip. Requires
/// Docker.
/// </summary>
public sealed class CurrentMatchQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task It_returns_the_jobs_with_a_current_match_against_the_cv_version()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var matches = new MatchRepository(
            seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await matches.UpsertAsync(seed.Match(seed.MatchedJobId));

        var query = new CurrentMatchQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var withMatch = await query.WithCurrentMatchAsync(
            seed.CvVersionId, [seed.MatchedJobId, seed.UnmatchedJobId]);

        // Only the matched job is reported; the one with no match is absent, so it stays matchable.
        withMatch.ShouldBe([seed.MatchedJobId]);
    }

    [RequiresDockerFact]
    public async Task A_staled_match_does_not_count_so_the_job_re_opens()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var matches = new MatchRepository(
            seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await matches.UpsertAsync(seed.Match(seed.MatchedJobId));

        // The re-staling sweep clears is_current for this CV version (a superseded CV, AC-08).
        (await matches.MarkNotCurrentForCvVersionAsync(seed.CvVersionId)).ShouldBe(1);

        var query = new CurrentMatchQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var withMatch = await query.WithCurrentMatchAsync(seed.CvVersionId, [seed.MatchedJobId]);

        // The staled match no longer counts — the job is matchable again.
        withMatch.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_match_against_a_different_cv_version_does_not_count()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var matches = new MatchRepository(
            seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await matches.UpsertAsync(seed.Match(seed.MatchedJobId));

        var query = new CurrentMatchQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var withMatch = await query.WithCurrentMatchAsync(Guid.CreateVersion7(), [seed.MatchedJobId]);

        // The lifecycle fact is scoped to the active CV version; another version's match is irrelevant.
        withMatch.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task An_empty_id_set_is_answered_without_a_round_trip()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var query = new CurrentMatchQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        (await query.WithCurrentMatchAsync(seed.CvVersionId, [])).ShouldBeEmpty();
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var profileId = Guid.CreateVersion7();
        var cvVersionId = Guid.CreateVersion7();
        var matchedJobId = Guid.CreateVersion7();
        var unmatchedJobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        AddJob(ctx, companyId, sourceId, matchedJobId, 'a');
        AddJob(ctx, companyId, sourceId, unmatchedJobId, 'b');
        ctx.Add(new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now));
        ctx.Add(new Profile(
            profileId, isActive: true, "Owner", salaryFloor: null, salaryFloorCurrency: null,
            TimezoneBand.EMEA, preferredCountries: ["Germany"], employmentTypes: [EmploymentType.FullTime], Now));
        ctx.Add(new CvVersion(
            cvVersionId, profileId, version: 1, isActive: true, "cv.pdf", "application/pdf",
            sizeBytes: 1024, contentHash: new string('a', 64), extractedText: "Extracted CV text.",
            uploadedAt: Now, activatedAt: Now));
        await ctx.SaveChangesAsync();

        return new Seed(database, runId, profileId, cvVersionId, matchedJobId, unmatchedJobId);
    }

    private static void AddJob(JobHunterDbContext ctx, Guid companyId, Guid sourceId, Guid jobId, char fingerprintFill)
    {
        var rawPostingId = Guid.CreateVersion7();
        ctx.Add(new RawPosting(
            rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"),
            "{\"t\":\"x\"}", 200, Now));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string(fingerprintFill, 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: Now, lastSeenAt: Now));
    }

    private sealed record Seed(
        TestDatabase Database, Guid RunId, Guid ProfileId, Guid CvVersionId, Guid MatchedJobId, Guid UnmatchedJobId)
    {
        public Match Match(Guid jobId) =>
            new(
                Guid.CreateVersion7(), jobId, RunId, ProfileId, CvVersionId,
                matchScore: 82, InterviewProbability.Good, missingSkills: ["Rust"],
                SalaryExpectation.TryCreate(100000m, 140000m, "EUR").Value,
                reasons: ["Strong platform-engineering overlap."], promptVersion: "match-v1", Now);
    }
}
