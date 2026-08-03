using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Sources;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the discovery cycle (SAD §6.1, AC-01): the sources due for a fetch this window. A
/// source is due when its company is active, its binding is live (not retired) and confident (≥ 0.80),
/// it is not currently quarantined, and it was not fetched since the recent-refetch cutoff — the last
/// clause is what makes two overlapping cycles fetch a source once. Dapper, flat DTO, read-only
/// (architecture rule 4 forbids a write method in this namespace); implements the Domain port.
/// </summary>
public sealed class DiscoveryCycleQuery(INpgsqlConnectionFactory connectionFactory) : IDiscoveryCycleQuery
{
    // Joined on the live binding so a company with only a retired or low-confidence binding never fans
    // out. quarantined_until is honoured against `now`, so an expired quarantine becomes due again on the
    // next cycle rather than being retried immediately. last_fetched_at NULL (never fetched) is always due.
    private const string Sql =
        """
        SELECT s.id AS SourceId, s.company_id AS CompanyId, b.ats_kind AS AtsKind
        FROM job_sources s
        JOIN companies c ON c.id = s.company_id
        JOIN ats_bindings b ON b.id = s.binding_id
        WHERE c.is_active
          AND b.retired_at IS NULL
          AND b.confidence >= @Threshold
          AND (s.quarantined_until IS NULL OR s.quarantined_until <= @Now)
          AND (s.last_fetched_at IS NULL OR s.last_fetched_at < @FetchedBefore)
        ORDER BY s.id
        """;

    public async Task<IReadOnlyList<DueSource>> DueSourcesAsync(
        DateTimeOffset now,
        DateTimeOffset fetchedBefore,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql,
            new
            {
                Threshold = BindingConfidence.DiscoveryThreshold,
                Now = now,
                FetchedBefore = fetchedBefore,
            },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<DueSource>(command);
        return rows.AsList();
    }
}
