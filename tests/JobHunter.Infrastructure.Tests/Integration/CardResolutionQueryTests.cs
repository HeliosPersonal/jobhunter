using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T10: the read side of "resolve a callback short id back to its card" (contract §Callback payloads,
/// AC-09). A callback carries only a signed short id, so the handler needs the candidate cards — their
/// <see cref="CardKey"/>, job id and apply URL — to HMAC-match against. This query returns the cards
/// delivered since a caller-supplied cutoff (the Telegram layer owns the window through <c>IClock</c>, so
/// the bound is explicit, never a silent cap), so a stale id from before the window simply does not
/// resolve. Read-only — Dapper never writes (architecture rule 4). Requires Docker.
/// </summary>
public sealed class CardResolutionQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 5, 0, 0, TimeSpan.Zero);

    [RequiresDockerFact]
    public async Task Candidates_returns_the_cards_of_a_recent_digest_with_their_keys_and_apply_urls()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var query = new CardResolutionQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        var candidates = await query.CandidatesSinceAsync(Now.AddHours(-24));

        var candidate = candidates.ShouldHaveSingleItem();
        candidate.JobId.ShouldBe(seed.JobId);
        candidate.Key.ShouldBe(CardKey.For(seed.RunId, seed.JobId));
        candidate.ApplyUrl.ShouldBe("https://acme.com/apply/1");
    }

    [RequiresDockerFact]
    public async Task Candidates_excludes_a_digest_generated_before_the_cutoff()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        var query = new CardResolutionQuery(new NpgsqlConnectionFactory(seed.Database.ConnectionString));
        // A cutoff after the digest's generation leaves nothing to resolve — a stale id falls out of scope.
        var candidates = await query.CandidatesSinceAsync(Now.AddHours(1));

        candidates.ShouldBeEmpty();
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var digestId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Now));
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, "acme", BindingConfidence.TryCreate(0.9m).Value, "{}", Now));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, "https://boards-api.greenhouse.io/v1/boards/acme/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, "job-1", ContentHash.Compute("{\"t\":\"x\"}"), "{\"t\":\"x\"}", 200, Now));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(new string('a', 64)).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: "https://acme.com/apply/1",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Remote, EmploymentType.FullTime, PostedAtGranularity.Day, firstSeenAt: Now, lastSeenAt: Now));
        ctx.Add(new Run(runId, Now.AddDays(-1), Now, ceilingUsd: 5m, Now));

        var card = new DigestCard(
            Guid.CreateVersion7(), digestId, jobId, runId, rank: 1, score: 82m,
            reasons: ["Strong platform fit."], applyUrlVerified: true);
        var digest = new Digest(
            digestId, runId, DigestMode.Full, totalNewJobs: 1, strongMatches: 1, avgSalaryUsd: null,
            suppressedCount: 0, suppressionBreakdown: [], carriedOverCount: 0, companiesChecked: 1,
            analysedCount: 1, degradedSources: [], narrative: null, NarrativeSource.Template,
            promptVersion: null, cards: [card], generatedAt: Now);
        ctx.Add(digest);
        await ctx.SaveChangesAsync();

        return new Seed(database, jobId, runId);
    }

    private sealed record Seed(TestDatabase Database, Guid JobId, Guid RunId);
}
