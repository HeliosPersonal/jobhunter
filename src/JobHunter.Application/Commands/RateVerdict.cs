namespace JobHunter.Application.Commands;

/// <summary>
/// What the <see cref="CommandRateLimiter"/> says about one command attempt (SAD §8). The distinction the
/// NFR turns on is between the <em>first</em> over-budget attempt in a window — which earns exactly one
/// throttle message — and every later attempt in that same window, which is <see cref="Silenced"/> so the
/// Owner is not spammed with one throttle reply per command (done-when #3).
/// </summary>
public enum RateVerdict
{
    /// <summary>Within budget — dispatch the command.</summary>
    Allowed = 1,

    /// <summary>The first over-budget attempt this window — refuse it and send one throttle message.</summary>
    Throttled = 2,

    /// <summary>A later over-budget attempt this window — refuse it silently; the message was already sent.</summary>
    Silenced = 3,
}
