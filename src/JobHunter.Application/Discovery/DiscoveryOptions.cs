namespace JobHunter.Application.Discovery;

/// <summary>
/// Tunables for the six-hourly discovery cycle (SAD §6.1, §8). Bound and validated at startup
/// (coding-standards §options). Defaults match the SAD: a fan-out concurrency of 8, a recent-refetch
/// window just under the 6-hour cadence so an overlapping cycle skips a source already fetched this
/// window, and a 24-hour quarantine after two consecutive failures.
/// </summary>
public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    /// <summary>Max sources fetched in parallel — <c>Parallel.ForEachAsync</c> degree (SAD §8).</summary>
    public int FetchConcurrency { get; init; } = 8;

    /// <summary>
    /// How recently a source must have been fetched to be skipped by an overlapping cycle. Slightly under
    /// the six-hour cadence so the next scheduled cycle always re-fetches, but a cycle that overruns and
    /// overlaps the next does not double-fetch.
    /// </summary>
    public TimeSpan RecentFetchWindow { get; init; } = TimeSpan.FromHours(5);

    /// <summary>How long a source stays quarantined after crossing the failure threshold (AC-08).</summary>
    public TimeSpan QuarantineFor { get; init; } = TimeSpan.FromHours(24);
}
