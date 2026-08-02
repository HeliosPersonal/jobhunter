using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The caching <see cref="IRobotsPolicy"/> (AC-06). It fetches <c>{scheme}://{host}/robots.txt</c> once
/// per host, parses it into <see cref="RobotsRules"/> and caches the result for 24 h (SAD §8). An
/// unreachable file is cached as <see cref="RobotsRules.AllowAll"/> (the permissive reading) and a
/// malformed/oversized one as <see cref="RobotsRules.DenyAll"/> (the conservative reading). The raw
/// fetch is an injected delegate so the whole policy is unit-tested with zero network.
/// </summary>
internal sealed class RobotsPolicy(
    RobotsPolicy.FetchRobots fetch,
    IMemoryCache cache,
    IOptions<PolitenessOptions> options) : IRobotsPolicy
{
    /// <summary>The result of fetching a <c>robots.txt</c>: its body, or a signal that it was unreachable.</summary>
    internal readonly record struct RobotsFetch(bool Reachable, bool Malformed, string Body)
    {
        public static RobotsFetch Ok(string body) => new(true, false, body);

        public static RobotsFetch NotReachable { get; } = new(false, false, string.Empty);

        public static RobotsFetch WasMalformed { get; } = new(true, true, string.Empty);
    }

    /// <summary>Fetches the raw <c>robots.txt</c> for an origin. Injected so tests never hit the network.</summary>
    internal delegate Task<RobotsFetch> FetchRobots(Uri robotsUrl, CancellationToken cancellationToken);

    private readonly PolitenessOptions _options = options.Value;

    public async Task<bool> IsAllowedAsync(Uri url, string userAgent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);

        var rules = await RulesForAsync(url, userAgent, cancellationToken).ConfigureAwait(false);
        return rules.IsAllowed(url.AbsolutePath);
    }

    private async Task<RobotsRules> RulesForAsync(Uri url, string userAgent, CancellationToken cancellationToken)
    {
        var cacheKey = $"robots:{url.Scheme}://{url.Authority}:{userAgent}";
        if (cache.TryGetValue(cacheKey, out RobotsRules? cached) && cached is not null)
        {
            return cached;
        }

        var robotsUrl = new Uri($"{url.Scheme}://{url.Authority}/robots.txt");
        var fetched = await fetch(robotsUrl, cancellationToken).ConfigureAwait(false);

        var rules = fetched switch
        {
            { Reachable: false } => RobotsRules.AllowAll,
            { Malformed: true } => RobotsRules.DenyAll,
            _ => RobotsRules.Parse(fetched.Body, userAgent),
        };

        cache.Set(cacheKey, rules, _options.RobotsCacheDuration);
        return rules;
    }
}
