using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the suppression-regret metric (F7 T09 done-when 5, risk D3). Implements
/// <see cref="ISuppressionRegretQuery"/> with Dapper, read-only (architecture rule 4 forbids a write here):
/// the count of the latest Run's suppressed jobs that carry at least one positive signal — an <c>Opened</c>,
/// <c>Saved</c>, <c>Applied</c>, <c>Interview</c> or <c>Offer</c>. A job the model hid, then retrieved and
/// acted on, is the evidence suppression was wrong (invariant 11).
///
/// <para>Only the latest Run's suppressed rows count, so an old suppression the current Run no longer makes
/// does not linger; a job is counted once however many times it was acted on (a semi-join, not a row count).
/// It selects <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, and it is
/// not this one (F4 invariant).</para>
/// </summary>
public sealed class SuppressionRegretQuery(INpgsqlConnectionFactory connectionFactory) : ISuppressionRegretQuery
{
    private static readonly string[] PositiveKinds =
    [
        SignalKind.Opened.ToString(),
        SignalKind.Saved.ToString(),
        SignalKind.Applied.ToString(),
        SignalKind.Interview.ToString(),
        SignalKind.Offer.ToString(),
    ];

    private const string Sql =
        """
        SELECT COUNT(*)::int
        FROM scores s
        WHERE s.suppressed
          AND s.run_id = (
              SELECT r.id FROM runs r
              ORDER BY r.started_at DESC NULLS LAST
              LIMIT 1
          )
          AND EXISTS (
              SELECT 1 FROM signals sig
              WHERE sig.job_id = s.job_id
                AND sig.kind = ANY(@Positive)
          )
        """;

    public async Task<int> LatestRunRegretCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { Positive = PositiveKinds }, cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<int>(command);
    }
}
