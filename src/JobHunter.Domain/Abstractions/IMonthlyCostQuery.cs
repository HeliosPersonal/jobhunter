using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind <c>/cost [month]</c> (F10 T09): a calendar month's spend broken down by pipeline stage
/// and model tier, each line carrying both the estimated and the actual dollars so the command can flag drift.
/// Read-only (Dapper) over the append-only cost ledger; defined in Domain so the command depends on the port,
/// not the SQL. The ledger is otherwise write-only through <c>IRunRepository</c> — this is its first read side.
/// </summary>
public interface IMonthlyCostQuery
{
    /// <param name="monthStart">
    /// The first instant of the calendar month, inclusive. The query sums ledger entries recorded in
    /// <c>[monthStart, monthStart + 1 month)</c> — a half-open window, so a month boundary is never
    /// double-counted.
    /// </param>
    Task<IReadOnlyList<CostBreakdownRow>> BreakdownForMonthAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default);
}
