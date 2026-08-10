using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of the weekly ratings-based <c>precision@10</c> metric (F4 T20 done-when 3, D5). Implements
/// <see cref="IWeeklyPrecisionQuery"/> with Dapper, read-only (architecture rule 4 forbids a write here). It
/// anchors on the latest <em>opened</em> rating round (<c>rating_round_log</c>) — precision only exists for a
/// week the Owner was actually prompted about — then measures over that week's top-ten delivered cards, joining
/// <c>delivery_log</c> → <c>digests</c> → <c>digest_cards</c> exactly as <see cref="WeeklyTopCardsQuery"/> does.
/// The numerator is how many of those cards carry a <c>Rated</c> signal; a "worth opening" tap writes one such
/// signal and "not worth" writes nothing, so counting <c>Rated</c> rows <em>is</em> counting the hits.
///
/// <para>No round ever opened yields no row — <c>null</c>, "not yet measured", never a misleading zero. A round
/// that opened but delivered nothing yields <c>Considered = 0</c> and precision <c>0</c> (the CASE guards the
/// division). The window is half-open <c>[week_start, week_start + 7d)</c> so two adjacent weeks never both
/// claim a boundary delivery, and the top-ten cap matches the metric's denominator. It selects
/// <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, not this one.</para>
/// </summary>
public sealed class WeeklyPrecisionQuery(INpgsqlConnectionFactory connectionFactory) : IWeeklyPrecisionQuery
{
    private const string Sql =
        """
        WITH latest_round AS (
            SELECT week_start
            FROM rating_round_log
            ORDER BY week_start DESC, opened_at DESC
            LIMIT 1
        ),
        top_ten AS (
            SELECT c.job_id,
                   EXISTS (
                       SELECT 1 FROM signals sig
                       WHERE sig.job_id = c.job_id AND sig.kind = @Rated
                   ) AS is_hit
            FROM latest_round lr
            JOIN delivery_log dl ON dl.delivered_at >= lr.week_start
                                AND dl.delivered_at < lr.week_start + INTERVAL '7 days'
            JOIN digests d ON d.run_id = dl.run_id
            JOIN digest_cards c ON c.digest_id = d.id AND c.card_key = dl.card_key
            ORDER BY c.rank
            LIMIT 10
        )
        SELECT lr.week_start                                              AS WeekStart,
               COUNT(t.job_id)::int                                      AS Considered,
               COUNT(t.job_id) FILTER (WHERE t.is_hit)::int              AS Hits,
               CASE WHEN COUNT(t.job_id) = 0 THEN 0
                    ELSE ROUND(COUNT(t.job_id) FILTER (WHERE t.is_hit)::numeric / COUNT(t.job_id), 4)
               END                                                        AS Precision
        FROM latest_round lr
        LEFT JOIN top_ten t ON true
        GROUP BY lr.week_start
        """;

    public async Task<WeeklyPrecision?> LatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql, new { Rated = SignalKind.Rated.ToString() }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<WeeklyPrecision?>(command);
    }
}
