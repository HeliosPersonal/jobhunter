using JobHunter.Domain.Sources;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind <c>/sources</c> (F10 T09, R4): each ATS provider's fetch health over a trailing window
/// — attempts, successes and the last attempt — grouped by provider so the Owner sees which integration is
/// degrading before it quarantines. Read-only (Dapper); defined in Domain so the command depends on the port,
/// not the SQL. Distinct from <see cref="IDegradedCoverageQuery"/>, which reports what is <em>already</em>
/// quarantined; this reports the raw attempt/success trend that precedes it.
/// </summary>
public interface ISourceHealthQuery
{
    /// <param name="since">
    /// The start of the trailing window; only fetch attempts started at or after this instant are counted. The
    /// caller passes <c>now − 24h</c> for the command's day view.
    /// </param>
    Task<IReadOnlyList<SourceHealth>> HealthSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
