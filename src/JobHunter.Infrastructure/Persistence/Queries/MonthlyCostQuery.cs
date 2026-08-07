using Dapper;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;

namespace JobHunter.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side of <c>/cost [month]</c> (F10 T09): a calendar month's spend rolled up by pipeline stage and
/// model tier, each line carrying both the estimated and the actual dollars so the command can flag drift. Dapper,
/// flat DTO, read-only (architecture rule 4 forbids a write here) over the append-only cost ledger — its first
/// read side, the ledger being otherwise write-only through <c>IRunRepository</c>. Implements the Domain port.
///
/// <para>The window is half-open — <c>[monthStart, monthStart + 1 month)</c> — so an entry recorded at the first
/// instant of the next month belongs to that month, never this one, and a boundary is never double-counted.
/// <c>stage</c> and <c>tier</c> are the persisted <c>text</c> enum names, so the roll-up groups on them directly;
/// a FILTER on <c>kind</c> splits the estimate from the actual within the one pass, and a stage/tier with no
/// entry of a kind reports <c>0</c> for it (COALESCE) rather than a null.</para>
/// </summary>
public sealed class MonthlyCostQuery(INpgsqlConnectionFactory connectionFactory) : IMonthlyCostQuery
{
    private const string Sql =
        """
        SELECT stage AS Stage,
               tier AS Tier,
               COALESCE(sum(cost_usd) FILTER (WHERE kind = 'Estimated'), 0) AS EstimatedUsd,
               COALESCE(sum(cost_usd) FILTER (WHERE kind = 'Actual'), 0) AS ActualUsd
        FROM cost_ledger_entries
        WHERE recorded_at >= @MonthStart AND recorded_at < @MonthEnd
        GROUP BY stage, tier
        ORDER BY stage, tier
        """;

    public async Task<IReadOnlyList<CostBreakdownRow>> BreakdownForMonthAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var command = new CommandDefinition(
            Sql,
            new { MonthStart = monthStart, MonthEnd = monthStart.AddMonths(1) },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CostBreakdownRow>(command);
        return rows.AsList();
    }
}
