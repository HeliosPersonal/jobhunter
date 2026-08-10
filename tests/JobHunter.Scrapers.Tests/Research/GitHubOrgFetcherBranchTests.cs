using JobHunter.Application.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Scrapers.Research;
using JobHunter.Scrapers.Tests.Support;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Research;

/// <summary>
/// The failure and shape-tolerance arms of the GitHub org fetcher (AC-07): a transport throw, a non-Ok
/// guard outcome, a JSON body that is not an array, entries that are not repo objects, repos with no name,
/// and each optional field (language, stars, description) present or absent. Every degenerate body is no
/// document, never an exception — and a repo missing an optional field simply omits that fragment.
/// </summary>
public sealed class GitHubOrgFetcherBranchTests
{
    private const string ReposUrl = "https://api.github.com/orgs/acme/repos?per_page=100&sort=pushed";

    private static readonly FakeClock Clock =
        new(new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));

    private static Company CompanyOn(string domain) =>
        new(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CanonicalDomain.TryCreate(domain).Value,
            "Acme Inc",
            CompanySource.Curated,
            Clock.UtcNow);

    private static GitHubOrgFetcher Fetcher(IGuardedResearchFetch guard) =>
        new(guard, Clock, NullLogger<GitHubOrgFetcher>.Instance);

    private static FakeGuardedResearchFetch Serving(string body) =>
        FakeGuardedResearchFetch.ServingHtml(new Dictionary<string, string> { [ReposUrl] = body });

    private static async Task<string> TextOrEmpty(string body)
    {
        var docs = await Fetcher(Serving(body)).FetchAsync(CompanyOn("acme.com"), CancellationToken.None);
        return docs.Count == 0 ? string.Empty : docs[0].Text;
    }

    [Fact]
    public async Task ATransportThrow_isCaught_asNoDocument()
    {
        var guard = FakeGuardedResearchFetch.NothingFound();
        guard.Throw = new Uri(ReposUrl);

        var docs = await Fetcher(guard).FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        docs.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARefusedOutcome_isNoDocument()
    {
        var guard = new FakeGuardedResearchFetch(_ => ResearchFetchResult.Refused("robots-disallowed"));

        var docs = await Fetcher(guard).FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        docs.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABodyThatIsNotAnArray_isNoDocument()
    {
        (await TextOrEmpty("""{ "message": "Not Found" }""")).ShouldBeEmpty();
    }

    [Fact]
    public async Task NonObjectArrayEntries_areSkipped_objectsStillRead()
    {
        var text = await TextOrEmpty("""[ 42, "loose", { "name": "real" } ]""");

        text.ShouldBe("real");
    }

    [Fact]
    public async Task ARepoWithNoName_isSkipped_theNamedOneSurvives()
    {
        // The first entry has no "name" property at all; the second must still land.
        var text = await TextOrEmpty("""[ { "language": "Go" }, { "name": "real" } ]""");

        text.ShouldBe("real");
    }

    [Fact]
    public async Task ARepoWhoseNameIsNotAString_isSkipped()
    {
        // "name" present but a number — ReadString yields empty, so the repo is skipped and there is no doc.
        (await TextOrEmpty("""[ { "name": 123 } ]""")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARepoWithOnlyAName_omitsLanguageStarsAndDescription()
    {
        var text = await TextOrEmpty("""[ { "name": "bare" } ]""");

        text.ShouldBe("bare");
    }

    [Fact]
    public async Task StarsThatAreNotANumber_areOmitted()
    {
        var text = await TextOrEmpty("""[ { "name": "widget", "stargazers_count": "lots" } ]""");

        text.ShouldBe("widget");
    }

    [Fact]
    public async Task StarsTooLargeForAnInt_areOmitted()
    {
        var text = await TextOrEmpty("""[ { "name": "widget", "stargazers_count": 99999999999 } ]""");

        text.ShouldBe("widget");
    }

    [Fact]
    public async Task AllOptionalFieldsPresent_areAllRendered()
    {
        var text = await TextOrEmpty(
            """[ { "name": "widget", "language": "Rust", "stargazers_count": 42, "description": "a thing" } ]""");

        text.ShouldBe("widget [Rust] 42 stars — a thing");
    }
}
