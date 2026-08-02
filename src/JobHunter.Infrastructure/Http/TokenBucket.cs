namespace JobHunter.Infrastructure.Http;

/// <summary>
/// The pure token-bucket arithmetic (SAD §8) — refill by elapsed time, take one token, report the wait
/// until the next token. Immutable and clock-free: the caller supplies "now", so the same logic drives
/// both the in-memory limiter (unit-tested here) and, transcribed to Lua, the distributed Redis bucket.
/// A full bucket holds <see cref="Capacity"/> tokens and refills at <see cref="RatePerSecond"/> tokens/s.
/// </summary>
internal readonly record struct TokenBucket(double Tokens, DateTimeOffset UpdatedAt, int RatePerSecond)
{
    /// <summary>The bucket's ceiling: one second's worth of budget, so bursts never exceed the rate.</summary>
    public int Capacity => RatePerSecond;

    /// <summary>A fresh, full bucket at <paramref name="now"/> for the given rate.</summary>
    public static TokenBucket Full(DateTimeOffset now, int ratePerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ratePerSecond, 1);
        return new TokenBucket(ratePerSecond, now, ratePerSecond);
    }

    /// <summary>
    /// Refills for the time elapsed since <see cref="UpdatedAt"/> and attempts to take one token.
    /// Returns the bucket after the attempt, whether a token was taken, and — when it was not — the wait
    /// until one becomes available. Never lets a clock going backwards remove tokens.
    /// </summary>
    public (TokenBucket Bucket, bool Granted, TimeSpan RetryAfter) TryTake(DateTimeOffset now)
    {
        var elapsed = now - UpdatedAt;
        var refilled = Tokens;
        if (elapsed > TimeSpan.Zero)
        {
            refilled = Math.Min(Capacity, Tokens + (elapsed.TotalSeconds * RatePerSecond));
        }

        if (refilled >= 1.0)
        {
            return (this with { Tokens = refilled - 1.0, UpdatedAt = now }, true, TimeSpan.Zero);
        }

        var needed = (1.0 - refilled) / RatePerSecond;
        return (this with { Tokens = refilled, UpdatedAt = now }, false, TimeSpan.FromSeconds(needed));
    }
}
