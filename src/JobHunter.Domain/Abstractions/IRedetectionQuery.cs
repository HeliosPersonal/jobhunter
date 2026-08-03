using JobHunter.Domain.Companies;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port that selects the companies due for binding re-detection this run (SAD §6.2, AC-05). A
/// company is due when its live binding was detected before <paramref name="staleBefore"/>, or when its
/// board returned zero postings on each of its last <paramref name="emptyCycles"/> successful fetches —
/// the "two consecutive empty cycles" signal that a board legitimately holding openings does not trip.
///
/// Re-detection is spread across the week rather than stampeding on one day: each company falls into a
/// fixed bucket by a hash of its id, and a run probes only the bucket for that day
/// (<paramref name="dayBucket"/> of <paramref name="bucketCount"/>). Read-only (Dapper); defined in Domain
/// so the re-detection handler depends on the port, not the SQL.
/// </summary>
public interface IRedetectionQuery
{
    /// <param name="staleBefore">A live binding detected at or after this instant is still fresh.</param>
    /// <param name="emptyCycles">
    /// How many consecutive most-recent successful fetches must have returned zero postings for a company
    /// with a fresh binding to still be a candidate (two, per AC-05).
    /// </param>
    /// <param name="dayBucket">The bucket to probe this run — companies not in it are deferred to their day.</param>
    /// <param name="bucketCount">The number of buckets the week is split into (seven, one per day).</param>
    Task<IReadOnlyList<RedetectionCandidate>> DueCandidatesAsync(
        DateTimeOffset staleBefore,
        int emptyCycles,
        int dayBucket,
        int bucketCount,
        CancellationToken cancellationToken = default);
}
