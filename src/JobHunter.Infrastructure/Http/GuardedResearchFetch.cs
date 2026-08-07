using System.Net;
using JobHunter.Application.Abstractions;
using JobHunter.Domain.Research;
using Microsoft.Extensions.Logging;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The one guarded path every company-research fetch goes through (SAD §5 S5, §8 Allowlist, §10 QG-3,
/// §11 risk D1) — the structural half of the SSRF defence. For the initial request and <em>every</em>
/// redirect it applies, in order: the HTTPS-only scheme check, the category host allowlist, and (through
/// F1's <see cref="PolitenessHandler"/>) the public-address check. Because the research client is
/// configured with <c>AllowAutoRedirect = false</c>, this class follows redirects itself, re-running all
/// three checks on each hop — a redirect from a public host into private space is refused <em>after</em>
/// the redirect, which is the classic bypass. The socket then resolves DNS once and connects to the
/// address it validated (<see cref="ResearchConnector"/>), closing the rebinding window.
///
/// <para>Every outcome is a value, never an exception (coding-standards §2): a refusal (scheme, allowlist,
/// SSRF, robots), a rate deferral, a non-success status and a redirect loop all come back as a
/// <see cref="ResearchFetchResult"/>, so the calling fetcher records the category as unavailable rather than
/// letting one dead or hostile source throw.</para>
/// </summary>
internal sealed class GuardedResearchFetch(
    IHttpClientFactory httpClientFactory,
    ILogger<GuardedResearchFetch> logger) : IGuardedResearchFetch
{
    /// <summary>The redirect budget for one fetch — enough for legitimate canonicalisation, bounded so a loop ends.</summary>
    private const int MaxRedirects = 5;

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ILogger<GuardedResearchFetch> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<ResearchFetchResult> FetchAsync(
        ResearchCategory category,
        Uri url,
        string companyDomain,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var client = _httpClientFactory.CreateClient(PoliteHttp.ResearchClientName);
        var current = url;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            // Pre-dispatch checks — a URL failing either is refused with no request made. The scheme check is
            // ours as well as the handler's so a non-HTTP scheme never even forms a request; the allowlist is
            // ours alone (the handler does not know the category), and it is re-applied on every redirect hop.
            if (!current.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Research fetch refused non-HTTPS target {Host} for {Category}",
                    current.Host, category);
                return ResearchFetchResult.Refused("insecure-scheme");
            }

            if (!ResearchHostAllowlist.IsAllowed(category, current, companyDomain))
            {
                _logger.LogWarning("Research fetch refused non-allowlisted host {Host} for {Category}",
                    current.Host, category);
                return ResearchFetchResult.Refused("host-not-allowlisted");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var status = response.StatusCode;

            // The politeness handler declined the fetch (robots, SSRF public-address check, insecure scheme):
            // a 403 with a machine reason. The SSRF refusal here is the per-hop public-address check — the one
            // that catches a redirect into private space, since the handler ran it against this hop's host.
            if (status == HttpStatusCode.Forbidden && response.ReasonPhrase is { } reason
                && reason is "robots-disallowed" or "ssrf-private-address" or "insecure-scheme")
            {
                _logger.LogInformation("Research fetch to {Host} refused ({Reason})", current.Host, reason);
                return ResearchFetchResult.Refused(reason);
            }

            if (status == HttpStatusCode.TooManyRequests && response.ReasonPhrase == "rate-deferred")
            {
                return ResearchFetchResult.Deferred(response.Headers.RetryAfter?.Delta);
            }

            if (IsRedirect(status) && response.Headers.Location is { } location)
            {
                // Resolve relative locations against the current URL, then loop — the next hop re-runs every
                // check above before it is ever fetched.
                current = new Uri(current, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return ResearchFetchResult.HttpError($"http-{(int)status}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ResearchFetchResult.Ok(body, current);
        }

        // The redirect budget was spent without reaching a terminal response — a loop or a chain too long.
        _logger.LogInformation("Research fetch to {Host} exceeded the redirect budget", url.Host);
        return ResearchFetchResult.HttpError("too-many-redirects");
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        (int)status is >= 300 and < 400;
}
