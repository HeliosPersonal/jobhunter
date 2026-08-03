using JobHunter.Domain.Sources;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the digest's degraded-coverage footer (SAD §6.3, AC-09): the sources currently
/// quarantined at the given instant — the companies whose boards are not being fetched, and why. Read-only
/// (Dapper); defined in Domain so the digest consumer depends on the port, not the SQL.
/// </summary>
public interface IDegradedCoverageQuery
{
    /// <param name="asOf">
    /// The instant the summary is read; a source is degraded when its <c>quarantined_until</c> is still in
    /// the future at this instant. A quarantine that has already expired is no longer degraded.
    /// </param>
    Task<IReadOnlyList<DegradedSource>> DegradedSourcesAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);
}
