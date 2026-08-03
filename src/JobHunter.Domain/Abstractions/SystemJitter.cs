namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The production <see cref="IJitter"/>: it extends a delay by a uniformly random fraction in
/// <c>[0, <see cref="JitterFraction"/>]</c>, using the thread-safe shared PRNG. Additive-only, so the
/// backoff ceiling is never undercut. A test substitutes a deterministic <see cref="IJitter"/> instead of
/// this type, so the spread is asserted without depending on the real PRNG.
/// </summary>
public sealed class SystemJitter : IJitter
{
    /// <summary>The maximum fraction of the base delay that jitter may add — up to 20% longer.</summary>
    public const double JitterFraction = 0.20;

    public TimeSpan Apply(TimeSpan baseDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var factor = 1.0 + (Random.Shared.NextDouble() * JitterFraction);
        return baseDelay * factor;
    }
}
