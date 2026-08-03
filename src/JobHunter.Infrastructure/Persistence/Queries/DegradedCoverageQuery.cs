using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Sources;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the degraded-coverage footer (SAD §6.3, AC-09): every source whose quarantine window
/// has not yet expired at <c>asOf</c>, joined to its company for a human-readable line. Dapper, flat DTO,
/// read-only (architecture rule 4 forbids a write method in this namespace); implements the Domain port.
/// </summary>
public sealed class DegradedCoverageQuery(INpgsqlConnectionFactory connectionFactory) : IDegradedCoverageQuery
{
    // A source is degraded while its quarantine is still in the future — an expired quarantine has been
    // (or will be) picked up as due again, so it no longer counts against coverage.
    private const string Sql =
        """
        SELECT s.id AS SourceId,
               s.company_id AS CompanyId,
               c.display_name AS CompanyName,
               b.ats_kind AS AtsKind,
               s.consecutive_failures::int AS ConsecutiveFailures,
               s.quarantined_until AS QuarantinedUntil
        FROM job_sources s
        JOIN companies c ON c.id = s.company_id
        JOIN ats_bindings b ON b.id = s.binding_id
        WHERE s.quarantined_until IS NOT NULL
          AND s.quarantined_until > @AsOf
        ORDER BY s.quarantined_until
        """;

    public async Task<IReadOnlyList<DegradedSource>> DegradedSourcesAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { AsOf = asOf }, cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<DegradedSource>(command);
        return rows.AsList();
    }
}
