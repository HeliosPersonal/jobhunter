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

    /// <summary>
    /// A live binding older than this is re-detected (SAD §6.2, AC-05). Seven days, so every binding is
    /// re-probed at least weekly and an ATS migration is caught within the week.
    /// </summary>
    public TimeSpan BindingMaxAge { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How many consecutive most-recent successful fetches must have returned zero postings for a company
    /// with a still-fresh binding to be re-detected anyway (AC-05). Two, so a board that legitimately has
    /// no openings for one cycle is not re-probed on that basis.
    /// </summary>
    public int RedetectionEmptyCycles { get; init; } = 2;

    /// <summary>
    /// The number of buckets re-detection is spread across — one per day of the week — so the weekly
    /// re-probe does not stampede on a single day (AC-05: "spread across the week"). A company is probed
    /// on the day matching its stable id-hash bucket.
    /// </summary>
    public int RedetectionBuckets { get; init; } = 7;
}
