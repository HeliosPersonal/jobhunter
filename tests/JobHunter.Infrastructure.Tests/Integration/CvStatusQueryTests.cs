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
/// F10 T08 / <c>/cv</c>: the read side of the CV status command. <see cref="CvStatusQuery"/> answers the active
/// CV's <strong>metadata only</strong> — version, activation date and how many current matches were computed
/// against it — and never its content: the SQL selects <c>version</c>, <c>activated_at</c> and a <c>count</c>,
/// and never <c>extracted_text</c>. A superseded match (its <c>is_current</c> flag cleared) must not be counted,
/// so the number the Owner sees is the current matched-against total. With no active CV the query returns null so
/// the command can say so plainly. Requires Docker.
/// </summary>
public sealed class CvStatusQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ActivatedAt = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task It_reports_the_active_version_activation_date_and_current_match_count()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var matches = new MatchRepository(
            seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await matches.UpsertAsync(seed.Match(seed.MatchedJobId));
        await matches.UpsertAsync(seed.Match(seed.SecondMatchedJobId));

        var query = new CvStatusQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var status = await query.ActiveAsync();

        status.ShouldNotBeNull();
        status.Version.ShouldBe((short)2);
        status.ActivatedAt.ShouldBe(ActivatedAt);
        status.MatchCount.ShouldBe(2);
    }

    [RequiresDockerFact]
    public async Task A_staled_match_is_not_counted()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;
        var matches = new MatchRepository(
            seed.Database.CreateContext(), new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        await matches.UpsertAsync(seed.Match(seed.MatchedJobId));
        await matches.UpsertAsync(seed.Match(seed.SecondMatchedJobId));

        // The re-staling sweep clears is_current for this CV version's matches (a superseded CV, AC-08).
        (await matches.MarkNotCurrentForCvVersionAsync(seed.CvVersionId)).ShouldBe(2);

        var query = new CvStatusQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var status = await query.ActiveAsync();

        status.ShouldNotBeNull();
        status.MatchCount.ShouldBe(0);
    }

    [RequiresDockerFact]
    public async Task With_no_active_cv_it_returns_null()
    {
        var database = await TestDatabase.CreateAsync();
        await using var _ = database;

        var query = new CvStatusQuery(new NpgsqlConnectionFactory(database.ConnectionString));
        (await query.ActiveAsync()).ShouldBeNull();
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
        var secondMatchedJobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        AddJob(ctx, companyId, sourceId, matchedJobId, 'a');
        AddJob(ctx, companyId, sourceId, secondMatchedJobId, 'b');
        ctx.Add(new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now));
        ctx.Add(new Profile(
            profileId, isActive: true, "Owner", salaryFloor: null, salaryFloorCurrency: null,
            TimezoneBand.EMEA, preferredCountries: ["Germany"], employmentTypes: [EmploymentType.FullTime], Now));
        // An earlier, superseded version, plus the active one — the query must report only the active version.
        ctx.Add(new CvVersion(
            Guid.CreateVersion7(), profileId, version: 1, isActive: false, "cv-old.pdf", "application/pdf",
            sizeBytes: 512, contentHash: new string('c', 64), extractedText: "Old CV text.",
            uploadedAt: Now.AddDays(-30), activatedAt: Now.AddDays(-30)));
        ctx.Add(new CvVersion(
            cvVersionId, profileId, version: 2, isActive: true, "cv.pdf", "application/pdf",
            sizeBytes: 1024, contentHash: new string('a', 64), extractedText: "Extracted CV text.",
            uploadedAt: ActivatedAt, activatedAt: ActivatedAt));
        await ctx.SaveChangesAsync();

        return new Seed(database, runId, profileId, cvVersionId, matchedJobId, secondMatchedJobId);
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
        TestDatabase Database, Guid RunId, Guid ProfileId, Guid CvVersionId, Guid MatchedJobId, Guid SecondMatchedJobId)
    {
        public Match Match(Guid jobId) =>
            new(
                Guid.CreateVersion7(), jobId, RunId, ProfileId, CvVersionId,
                matchScore: 82, InterviewProbability.Good, missingSkills: ["Rust"],
                SalaryExpectation.TryCreate(100000m, 140000m, "EUR").Value,
                reasons: ["Strong platform-engineering overlap."], promptVersion: "match-v1", Now);
    }
}
