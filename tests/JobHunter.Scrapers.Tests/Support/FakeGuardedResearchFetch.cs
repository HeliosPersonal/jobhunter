using JobHunter.Application.Abstractions;
using JobHunter.Domain.Research;

namespace JobHunter.Scrapers.Tests.Support;

/// <summary>
/// A zero-network <see cref="IGuardedResearchFetch"/> for the category-fetcher suites. It answers each URL
/// from a routing function, records every URL it was asked for (so a test can assert which candidate paths
/// a fetcher tried and that a refused target is still <em>attempted</em> through the guard), and can be told
/// to throw for a URL — proving a fetcher isolates one dead candidate (AC-07). The guard's own SSRF and
/// allowlist behaviour is proven in the Infrastructure suite; here we prove the fetcher's use of it.
/// </summary>
internal sealed class FakeGuardedResearchFetch(Func<Uri, ResearchFetchResult> route) : IGuardedResearchFetch
{
    private readonly Func<Uri, ResearchFetchResult> _route = route;

    public List<(ResearchCategory Category, Uri Url, string CompanyDomain)> Calls { get; } = [];

    public Uri? Throw { get; set; }

    public Task<ResearchFetchResult> FetchAsync(
        ResearchCategory category,
        Uri url,
        string companyDomain,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((category, url, companyDomain));

        if (Throw is not null && url == Throw)
        {
            throw new HttpRequestException("connection refused");
        }

        return Task.FromResult(_route(url));
    }

    /// <summary>Every URL 404s — the "source has nothing" default.</summary>
    public static FakeGuardedResearchFetch NothingFound() =>
        new(_ => ResearchFetchResult.HttpError("http-404"));

    /// <summary>Answers exactly the given URLs with an OK HTML body; everything else is a 404.</summary>
    public static FakeGuardedResearchFetch ServingHtml(IReadOnlyDictionary<string, string> bodyByUrl) =>
        new(url => bodyByUrl.TryGetValue(url.ToString(), out var body)
            ? ResearchFetchResult.Ok(body, url)
            : ResearchFetchResult.HttpError("http-404"));
}
