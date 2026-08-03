using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read side of an offline reprocessing run (AC-09, QG-3): the live and closed jobs whose stored origin
/// payload is to be re-normalised, streamed so a large history recomputes with bounded memory and meets the
/// ≥ 5 000 postings/min target. Quarantined and superseded jobs are excluded — reprocessing never disturbs a
/// terminal state. Dapper, read-only (architecture rule 4 forbids a write in the Queries namespace); defined
/// in Domain so the reprocessing service depends on the port, not the Infrastructure query.
/// </summary>
public interface IReprocessableJobsQuery
{
    /// <summary>
    /// Streams every reprocessable job created at or after <paramref name="firstSeenFrom"/>, oldest first, so
    /// a run can be scoped to a period. The origin raw posting id lets the service re-read the exact payload
    /// the job was built from without contacting any provider.
    /// </summary>
    IAsyncEnumerable<ReprocessableJob> StreamAsync(
        DateTimeOffset firstSeenFrom,
        CancellationToken cancellationToken = default);
}
