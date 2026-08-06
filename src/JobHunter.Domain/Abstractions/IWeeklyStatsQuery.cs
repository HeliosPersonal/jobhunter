using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind <c>/stats</c> (F5 T11): the Owner's engagement over a half-open time window, counted
/// from the append-only <c>delivery_log</c> and the <c>signals</c> the card actions and the applied outcome
/// write. The command asks for two windows — this week and the week before — and computes the precision and
/// the week-over-week trend from them, so the port stays a plain windowed count and the arithmetic stays out
/// of the SQL. Read-only (Dapper, architecture rule 4); defined in Domain so the handler depends on the port,
/// not the query.
///
/// <para>It selects <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and it is
/// not this one (F4 invariant).</para>
/// </summary>
public interface IWeeklyStatsQuery
{
    /// <summary>
    /// The engagement in the half-open window <c>[from, to)</c>: delivered cards and the opened/ignored/saved
    /// reactions and applied outcomes recorded within it.
    /// </summary>
    Task<WeeklyEngagement> EngagementAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
