using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of "a company's live jobs" (data-model §jobs), backing the company-detail page's live
/// jobs section (F9 T06). It returns only <c>Live</c> jobs for the company — a closed or quarantined job
/// is never returned — most recent first. Dapper, flat DTO, read-only (architecture rule 4 forbids a
/// write method here); implements the port. The never-displayed normalised title is not selected.
/// </summary>
public sealed class CompanyJobsQuery(INpgsqlConnectionFactory connectionFactory) : ICompanyJobsQuery
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
        WHERE status = 'Live' AND company_id = @CompanyId
        ORDER BY first_seen_at DESC
        """;

    public async Task<IReadOnlyList<LiveJob>> LiveForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { CompanyId = companyId }, cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<LiveJob>(command);
        return rows.AsList();
    }
}
