namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The count of currently-live jobs in PostgreSQL, the authoritative side of the reconcile comparison
/// (F9-T08, SAD §6.3 "count live jobs"). Separate from <see cref="IJobProjectionSource.ProjectLiveAsync"/>
/// because reconcile needs only the cardinality to decide whether to re-index, and streaming every
/// projection just to count would defeat the point of a cheap nightly check. Read-only (Dapper,
/// architecture rule 4); defined in Domain so the reconcile service depends on the port, not the query.
/// </summary>
public interface ILiveJobCounter
{
    /// <summary>The number of jobs whose status is <c>Live</c>. Closed and quarantined jobs are excluded.</summary>
    Task<long> CountLiveAsync(CancellationToken cancellationToken = default);
}
