using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of <c>/hidden</c> (F7 T08 C5, done-when 6, risk D3): the jobs the most recent Run suppressed,
/// each with the reason it was withheld. Implements <see cref="IHiddenJobsQuery"/> with Dapper, read-only
/// (architecture rule 4 forbids a write here): the suppressed <c>scores</c> of the latest Run — the one with
/// the greatest <c>started_at</c> — joined back to <c>jobs</c> for the title and <c>companies</c> for the
/// display name.
///
/// <para>Only suppressed rows are selected (<c>WHERE s.suppressed</c>) — a shown job is not hidden — and only
/// the latest Run's, so an old suppression the current Run no longer makes does not linger and misreport
/// regret. Ordered by <c>final_score DESC, job_id</c> so the near-misses are at the top and the order is
/// deterministic, and capped at the caller's limit. It selects <strong>nothing about the Owner's CV</strong> —
/// the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class HiddenJobsQuery(INpgsqlConnectionFactory connectionFactory) : IHiddenJobsQuery
{
    private const string Sql =
        """
        SELECT s.job_id           AS JobId,
               j.title            AS Title,
               c.display_name     AS Company,
               s.final_score      AS Score,
               s.suppression_reason AS SuppressionReason
        FROM scores s
        JOIN jobs j ON j.id = s.job_id
        JOIN companies c ON c.id = j.company_id
        WHERE s.suppressed
          AND s.run_id = (
              SELECT r.id FROM runs r
              ORDER BY r.started_at DESC NULLS LAST
              LIMIT 1
          )
        ORDER BY s.final_score DESC, s.job_id
        LIMIT @Limit
        """;

    public async Task<IReadOnlyList<HiddenJob>> HiddenAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Limit = limit }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<HiddenRow>(command);

        return rows
            .Select(r => new HiddenJob(r.JobId, r.Title, r.Company, r.Score, r.SuppressionReason.Trim()))
            .ToList();
    }

    private sealed record HiddenRow(
        Guid JobId,
        string Title,
        string Company,
        decimal Score,
        string SuppressionReason);
}
