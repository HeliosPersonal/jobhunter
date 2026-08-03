namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The single source of randomised timing spread in the system. The batch poller re-enqueues itself on a
/// deterministic backoff schedule; jitter is added on top so several batches submitted in the same Run do
/// not poll the provider in lockstep (F3 SAD §8). Behind a port so a test drives a deterministic spread
/// and no production code reaches for the ambient <see cref="System.Random"/> directly.
/// </summary>
public interface IJitter
{
    /// <summary>
    /// Returns <paramref name="baseDelay"/> extended by a small random fraction. Jitter only ever adds to
    /// a delay — never shortens it below the schedule — so the backoff ceiling is honoured while the
    /// spread breaks synchrony. A zero base delay stays zero.
    /// </summary>
    TimeSpan Apply(TimeSpan baseDelay);
}
