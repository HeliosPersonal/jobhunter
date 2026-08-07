using JobHunter.Application.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Research;
using JobHunter.Scrapers.Research;
using JobHunter.Scrapers.Tests.Support;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Research;

/// <summary>
/// The GitHub organisation fetcher (SAD §5, contract §Fetcher set): the one third-party company-research
/// source with a public, auth-free API. It derives the org login from the company's registrable domain,
/// queries the org's public repositories through the guarded fetch path (host <c>api.github.com</c>, which
/// the OpenSource allowlist permits), and turns the repository list into one document. Every failure —
/// the org not existing, an error status, an empty or malformed body — is no document, never an exception.
/// </summary>
public sealed class GitHubOrgFetcherTests
{
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

    [Fact]
    public void Category_isOpenSource()
    {
        Fetcher(FakeGuardedResearchFetch.NothingFound()).Category.ShouldBe(ResearchCategory.OpenSource);
    }

    [Fact]
    public async Task QueriesTheOrgReposEndpoint_onApiGithubCom_derivedFromTheDomain()
    {
        var guard = FakeGuardedResearchFetch.NothingFound();

        await Fetcher(guard).FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        var call = guard.Calls.ShouldHaveSingleItem();
        call.Category.ShouldBe(ResearchCategory.OpenSource);
        call.Url.ToString().ShouldBe("https://api.github.com/orgs/acme/repos?per_page=100&sort=pushed");
    }

    [Fact]
    public async Task AListOfRepos_becomesOneDocument_summarisingThePublicPresence()
    {
        const string body = """
            [
              { "name": "widgets", "html_url": "https://github.com/acme/widgets",
                "description": "Core widget library", "language": "C#", "stargazers_count": 1200, "fork": false },
              { "name": "forked-thing", "html_url": "https://github.com/acme/forked-thing",
                "description": "a fork", "language": "Go", "stargazers_count": 3, "fork": true }
            ]
            """;
        var guard = FakeGuardedResearchFetch.ServingHtml(new Dictionary<string, string>
        {
            ["https://api.github.com/orgs/acme/repos?per_page=100&sort=pushed"] = body,
        });

        var docs = await Fetcher(guard).FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        var doc = docs.ShouldHaveSingleItem();
        doc.Url.ShouldBe("https://api.github.com/orgs/acme/repos?per_page=100&sort=pushed");
        doc.ObservedAt.ShouldBe(Clock.UtcNow);
        doc.Text.ShouldContain("widgets");
        doc.Text.ShouldContain("Core widget library");
        doc.Text.ShouldContain("C#");
        doc.Text.ShouldContain("1200");
        // Forks are not the org's own work — excluded so the presence is not inflated.
        doc.Text.ShouldNotContain("forked-thing");
    }

    [Fact]
    public async Task AnEmptyRepoList_isNoDocument()
    {
        var guard = FakeGuardedResearchFetch.ServingHtml(new Dictionary<string, string>
        {
            ["https://api.github.com/orgs/acme/repos?per_page=100&sort=pushed"] = "[]",
        });

        var docs = await Fetcher(guard).FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        docs.ShouldBeEmpty();
    }

    [Fact]
    public async Task AMissingOrg_404_isNoDocument()
    {
        var docs = await Fetcher(FakeGuardedResearchFetch.NothingFound())
            .FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        docs.ShouldBeEmpty();
    }

    [Fact]
    public async Task AMalformedBody_isNoDocument_notAThrow()
    {
        var guard = FakeGuardedResearchFetch.ServingHtml(new Dictionary<string, string>
        {
            ["https://api.github.com/orgs/acme/repos?per_page=100&sort=pushed"] = "{ not json",
        });

        var docs = await Fetcher(guard).FetchAsync(CompanyOn("acme.com"), CancellationToken.None);

        docs.ShouldBeEmpty();
    }
}
