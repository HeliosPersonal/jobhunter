using JobHunter.Domain.Search;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read side that assembles the flat <see cref="JobProjectionSource"/> a <see cref="JobDocument"/> is
/// projected from (data-model §Projection, F9-T02). It is the single query the indexer and the rebuild
/// both use, so the document is always derived from the same fields — the whole of QG-1: the index holds
/// nothing that is not re-derivable from PostgreSQL by this one call. Read-only (Dapper, architecture
/// rule 4). Returns <c>null</c> when the job no longer exists, so an index request for a vanished job
/// becomes a delete rather than an error.
/// </summary>
public interface IJobProjectionSource
{
    /// <summary>Projects one job by id, or <c>null</c> if it no longer exists.</summary>
    Task<JobProjectionSource?> ProjectAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams every currently-live job's projection source for a full rebuild (QG-1). Ordered by id so a
    /// rebuild is deterministic and a partial rebuild is resumable.
    /// </summary>
    IAsyncEnumerable<JobProjectionSource> ProjectLiveAsync(CancellationToken cancellationToken = default);
}
