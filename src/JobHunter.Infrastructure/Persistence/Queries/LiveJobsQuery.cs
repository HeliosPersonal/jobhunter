using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "jobs discovered since the previous Run cut-off" (data-model §jobs). It returns only
/// <c>Live</c> jobs first seen at or after the cutoff — a closed or quarantined job is never returned — so
/// the query is served by the partial index <c>idx_jobs_first_seen</c> (<c>WHERE status='Live'</c>) and
/// scans only what it returns. Dapper, flat DTO, read-only (architecture rule 4 forbids a write method
/// here); implements the port. The never-displayed normalised title is deliberately not selected.
/// </summary>
public sealed class LiveJobsQuery(INpgsqlConnectionFactory connectionFactory) : ILiveJobsQuery
{
    private const string Sql =
        """
        SELECT id AS Id,
               company_id AS CompanyId,
               title AS Title,
               seniority AS Seniority,
               remote_policy AS RemotePolicy,
               employment_type AS EmploymentType,
               apply_url AS ApplyUrl,
               first_seen_at AS FirstSeenAt,
               last_seen_at AS LastSeenAt
        FROM jobs
        WHERE status = 'Live' AND first_seen_at >= @Since
        ORDER BY first_seen_at DESC
        """;

    public async Task<IReadOnlyList<LiveJob>> DiscoveredSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Since = since }, cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<LiveJob>(command);
        return rows.AsList();
    }
}
