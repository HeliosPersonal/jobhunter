using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over live jobs (data-model §jobs, access pattern "new since last Run"). Read-only
/// (Dapper); excludes closed and quarantined jobs, and is served by the partial index
/// <c>idx_jobs_first_seen</c> filtered on <c>status='Live'</c> so it scans only what it returns. Defined
/// in Domain so consumers depend on the port, not the Infrastructure query.
/// </summary>
public interface ILiveJobsQuery
{
    /// <summary>
    /// The live jobs first seen at or after <paramref name="since"/>, most recent first. A closed or
    /// quarantined job is never returned regardless of when it was seen.
    /// </summary>
    Task<IReadOnlyList<LiveJob>> DiscoveredSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
