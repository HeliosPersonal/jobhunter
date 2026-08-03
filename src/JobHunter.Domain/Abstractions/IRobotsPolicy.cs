namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The <c>robots.txt</c> gate (invariant 10, AC-06). Parsed and cached per host for 24 h; a disallowed
/// path is never fetched. An unreachable <c>robots.txt</c> is read permissively (allow); a malformed one
/// is read conservatively (disallow) — we would rather miss a board than crawl one that asked us not to.
/// </summary>
public interface IRobotsPolicy
{
    /// <summary>
    /// True when <paramref name="userAgent"/> is permitted to fetch <paramref name="url"/> under the
    /// origin's <c>robots.txt</c>. The decision is the one the caller records before it fetches.
    /// </summary>
    Task<bool> IsAllowedAsync(Uri url, string userAgent, CancellationToken cancellationToken = default);
}
