using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of <c>/stats</c> (F5 T11). Implements <see cref="IWeeklyStatsQuery"/> with Dapper, read-only
/// (architecture rule 4 forbids a write here): one row of counts for a half-open window <c>[from, to)</c> —
/// deliveries from the append-only <c>delivery_log</c> (invariant 8) and the opened/ignored/saved reactions
/// and applied outcomes from <c>signals</c> of the matching kinds. The window is half-open so two adjacent
/// weeks never both claim a boundary row, which is what makes the command's week-over-week comparison sound.
///
/// <para>The counts are read in a single round trip. It selects <strong>nothing about the Owner</strong> —
/// the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public sealed class WeeklyStatsQuery(INpgsqlConnectionFactory connectionFactory) : IWeeklyStatsQuery
{
    private const string Sql =
        """
        SELECT
            (SELECT count(*)::int FROM delivery_log d
                WHERE d.delivered_at >= @From AND d.delivered_at < @To) AS Delivered,
            (SELECT count(*)::int FROM signals s
                WHERE s.kind = 'Opened'  AND s.occurred_at >= @From AND s.occurred_at < @To) AS Opened,
            (SELECT count(*)::int FROM signals s
                WHERE s.kind = 'Ignored' AND s.occurred_at >= @From AND s.occurred_at < @To) AS Ignored,
            (SELECT count(*)::int FROM signals s
                WHERE s.kind = 'Saved'   AND s.occurred_at >= @From AND s.occurred_at < @To) AS Saved,
            (SELECT count(*)::int FROM signals s
                WHERE s.kind = 'Applied' AND s.occurred_at >= @From AND s.occurred_at < @To) AS Applied
        """;

    public async Task<WeeklyEngagement> EngagementAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(Sql, new { From = from, To = to }, cancellationToken: cancellationToken);
        var row = await connection.QuerySingleAsync<CountsRow>(command);

        return new WeeklyEngagement(row.Delivered, row.Opened, row.Ignored, row.Saved, row.Applied);
    }

    private sealed record CountsRow(int Delivered, int Opened, int Ignored, int Saved, int Applied);
}
