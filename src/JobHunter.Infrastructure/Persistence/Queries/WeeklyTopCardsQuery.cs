using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the weekly precision@10 rating loop (F4 T20). Implements <see cref="IWeeklyTopCardsQuery"/>
/// with Dapper, read-only (architecture rule 4 forbids a write here): the previous week's top-ten
/// <em>delivered</em> cards, joining the append-only <c>delivery_log</c> (bounded to the half-open window
/// <c>[from, to)</c> on <c>delivered_at</c>, invariant 8) to its <c>digests</c> row on <c>run_id</c> and to
/// <c>digest_cards</c> on <c>(digest_id, card_key)</c>. The join on <c>card_key</c> naturally excludes the
/// reserved header and footer deliveries — they have no backing card — so only real job cards are returned.
///
/// <para>Ordered by <c>rank</c> and capped at ten: precision@10 is measured over ten cards even on a week that
/// delivered more. The window is half-open so two adjacent weeks never both claim a boundary delivery. It
/// selects <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is not this
/// one (F4 invariant).</para>
/// </summary>
public sealed class WeeklyTopCardsQuery(INpgsqlConnectionFactory connectionFactory) : IWeeklyTopCardsQuery
{
    private const string Sql =
        """
        SELECT c.job_id AS JobId,
               d.run_id AS RunId,
               c.rank::int AS Rank
        FROM delivery_log dl
        JOIN digests d ON d.run_id = dl.run_id
        JOIN digest_cards c ON c.digest_id = d.id AND c.card_key = dl.card_key
        WHERE dl.delivered_at >= @From AND dl.delivered_at < @To
        ORDER BY c.rank
        LIMIT 10
        """;

    public async Task<IReadOnlyList<WeeklyTopCard>> TopCardsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { From = from, To = to }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<WeeklyTopCard>(command);

        return rows.ToList();
    }
}
