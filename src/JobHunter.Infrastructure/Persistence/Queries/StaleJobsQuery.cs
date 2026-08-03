using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the job-liveness check (SAD §6.2, T08): the live jobs whose <em>every</em> alias has
/// gone stale — the latest <c>job_aliases.last_seen_at</c> across the job is strictly before the cutoff, so
/// the opening no longer appears on any board that carried it. A job with even one fresh alias has a
/// <c>MAX(last_seen_at)</c> at or after the cutoff and is excluded (data-model §job_aliases). The closure
/// instant returned is that same <c>MAX</c>, which the handler records as <c>closed_at</c> and reuses as the
/// closure's idempotency component.
///
/// <para>Closure is suspended for a job any of whose contributing sources is still quarantined
/// (data-model §D4): a provider outage stops a source being fetched, which would make its jobs look stale.
/// The <c>NOT EXISTS</c> excludes any candidate with an alias on a source whose <c>quarantined_until</c> is
/// in the future at the cutoff. Dapper, flat DTO, read-only (architecture rule 4 forbids a write here);
/// implements the port.</para>
/// </summary>
public sealed class StaleJobsQuery(INpgsqlConnectionFactory connectionFactory) : IStaleJobsQuery
{
    private const string Sql =
        """
        SELECT j.id AS JobId, MAX(a.last_seen_at) AS LastSeenAt
        FROM jobs j
        JOIN job_aliases a ON a.job_id = j.id
        WHERE j.status = 'Live'
          AND NOT EXISTS (
              SELECT 1
              FROM job_aliases qa
              JOIN job_sources s ON s.id = qa.source_id
              WHERE qa.job_id = j.id
                AND s.quarantined_until IS NOT NULL
                AND s.quarantined_until > @QuarantinedAsOf
          )
        GROUP BY j.id
        HAVING MAX(a.last_seen_at) < @SeenBefore
        ORDER BY j.id
        """;

    public async Task<IReadOnlyList<StaleJob>> StaleSinceAsync(
        DateTimeOffset seenBefore,
        DateTimeOffset quarantinedAsOf,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql,
            new { SeenBefore = seenBefore, QuarantinedAsOf = quarantinedAsOf },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<StaleJob>(command);
        return rows.AsList();
    }
}
