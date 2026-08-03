using JobHunter.Domain.Sources;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port that selects the sources due for a fetch this cycle (SAD §6.1, AC-01). A source is due
/// when its company is active, its binding is live and confident (≥ 0.80), it is not currently
/// quarantined, and it was not fetched since <paramref name="fetchedBefore"/> — the last clause is what
/// makes two overlapping six-hourly cycles fetch a source once rather than twice. Read-only (Dapper);
/// defined in Domain so the <c>DiscoveryCycleHandler</c> depends on the port, not the SQL.
/// </summary>
public interface IDiscoveryCycleQuery
{
    /// <param name="now">The current instant; a quarantine expired by <paramref name="now"/> is due again.</param>
    /// <param name="fetchedBefore">
    /// The recent-refetch cutoff: a source whose <c>last_fetched_at</c> is at or after this instant was
    /// already fetched this cycle and is skipped. Sources never fetched (null) are always due.
    /// </param>
    Task<IReadOnlyList<DueSource>> DueSourcesAsync(
        DateTimeOffset now,
        DateTimeOffset fetchedBefore,
        CancellationToken cancellationToken = default);
}
