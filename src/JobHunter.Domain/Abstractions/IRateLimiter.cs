namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The per-host rate budget (SAD §8, invariant 10). Rate limiting lives behind this port and inside the
/// shared HTTP handler, never inside an adapter, so a new adapter physically cannot bypass the budget
/// (QG-2). The default is one request per second per host; a source may declare a lower rate.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to take one token for <paramref name="host"/>. A granted lease means the request may go
    /// out now; a denied lease carries the delay after which a token will be available, so the caller
    /// defers rather than drops (T04 DoD).
    /// </summary>
    Task<RateLease> AcquireAsync(string host, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a provider-declared cool-off: the host is blocked for at least <paramref name="retryAfter"/>.
    /// The penalty never shortens an existing, longer block — a <c>Retry-After</c> is honoured exactly and
    /// is never overridden by our own shorter backoff (AC-07).
    /// </summary>
    Task PenaliseAsync(string host, TimeSpan retryAfter, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a token acquisition: granted now, or deferred by <see cref="RetryAfter"/>.</summary>
public readonly record struct RateLease(bool Granted, TimeSpan RetryAfter)
{
    /// <summary>A granted lease — the request may proceed immediately.</summary>
    public static RateLease Allow { get; } = new(true, TimeSpan.Zero);

    /// <summary>A denied lease — no token is available for <paramref name="retryAfter"/>.</summary>
    public static RateLease Deny(TimeSpan retryAfter) => new(false, retryAfter);
}
