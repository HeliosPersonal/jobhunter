using JobHunter.Domain.Preferences;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read side of the outcome-signal backfill (F7 T03, done-when 5): the terminal application outcomes
/// that have no matching signal yet, streamed oldest first so a large history replays with bounded memory.
/// Only outcomes missing a <c>(job_id, kind, occurred_at)</c> signal are returned, so a run over a fully
/// migrated history yields nothing — the query, not just the capture, is idempotent. Dapper, read-only
/// (architecture rule 4 forbids a write in the Queries namespace); defined in Domain so the backfill service
/// depends on the port, not the Infrastructure query.
/// </summary>
public interface IBackfillableOutcomeQuery
{
    /// <summary>
    /// Streams every terminal outcome transition that occurred at or after <paramref name="occurredFrom"/>
    /// and does not already have a captured signal, oldest first, so a run can be scoped to a period.
    /// </summary>
    IAsyncEnumerable<BackfillableOutcome> StreamAsync(
        DateTimeOffset occurredFrom,
        CancellationToken cancellationToken = default);
}
