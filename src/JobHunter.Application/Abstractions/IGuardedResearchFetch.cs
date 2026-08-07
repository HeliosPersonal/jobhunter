using JobHunter.Domain.Research;

namespace JobHunter.Application.Abstractions;

/// <summary>
/// The one guarded path every company-research fetch goes through (SAD §5 S5, §8 Allowlist, QG-3, risk
/// D1). It is the second, structural half of the SSRF defence: a category fetcher in
/// <c>JobHunter.Scrapers</c> hands it a URL and the category, and it applies the scheme check, the
/// category host allowlist and the public-address check to <em>every</em> hop — the initial request and
/// every redirect — resolving DNS once and connecting to the address it validated, so a redirect into
/// private space or a name that rebinds to a private address after validation cannot slip through.
///
/// <para>It lives in Application as a port because both Infrastructure (which implements it over F1's
/// politeness pipeline) and Scrapers (which consume it) reference this layer, but neither references the
/// other. A fetcher that asks for this port is safe by construction; it cannot build its own client
/// (QG-2, enforced by an architecture test).</para>
/// </summary>
public interface IGuardedResearchFetch
{
    /// <summary>
    /// Fetches <paramref name="url"/> for <paramref name="category"/> while researching a company whose
    /// canonical domain is <paramref name="companyDomain"/>, classifying the outcome as a value. Refuses —
    /// without making the request — a non-HTTPS scheme, a host outside the category allowlist, or any hop
    /// that resolves to a non-public address, re-checking after every redirect (SAD §11 D1).
    /// </summary>
    Task<ResearchFetchResult> FetchAsync(
        ResearchCategory category,
        Uri url,
        string companyDomain,
        CancellationToken cancellationToken = default);
}
