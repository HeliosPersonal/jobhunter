using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port for the re-match backlog (ADR-F4-0002, data-model §cv_versions). On CV activation the
/// re-match scheduler enqueues the recent live jobs; the next Run's matching scope drains what is queued
/// and marks each item consumed. Enqueue is idempotent on <c>(job_id) WHERE NOT consumed</c>, so
/// re-uploading a CV twice before a Run drains the backlog never queues a job twice. Defined in Domain so
/// the scheduler depends on the port, not the Infrastructure repository.
/// </summary>
public interface IReMatchBacklog
{
    /// <summary>
    /// Enqueues <paramref name="item"/> unless an unconsumed item already exists for its job. Returns
    /// <see langword="true"/> when a row was written and <see langword="false"/> on the idempotent no-op —
    /// the same first-write-versus-replay signal the match and score upserts return.
    /// </summary>
    Task<bool> EnqueueAsync(ReMatchQueueItem item, CancellationToken cancellationToken = default);

    /// <summary>The job ids of every unconsumed queued item — the extra jobs the next Run folds into its matching scope.</summary>
    Task<IReadOnlyList<Guid>> PendingJobIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks the unconsumed items for the given jobs as drained once the Run has taken them into scope; returns the number marked.</summary>
    Task<int> MarkConsumedAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default);
}
