using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the weekly ratings-based <c>precision@10</c> metric (F4 T20 done-when 3,
/// [[docs/DECISION-LOG|D5]]). It returns the latest opened rating round's precision: over that week's top-ten
/// <em>delivered</em> cards, how many carry a <c>Rated</c> signal ("worth opening") against how many were
/// delivered. It reads the same delivered top-ten <see cref="IWeeklyTopCardsQuery"/> prompts, joined to the
/// <c>Rated</c> signals those cards drew — so the number charted is exactly the one the Owner produced.
///
/// <para>Only a week that has actually been opened for rating has a precision; a system that has never run a
/// rating round returns <c>null</c> rather than a misleading zero. Read-only (Dapper, architecture rule 4);
/// defined in Domain so the reporter depends on the port, not the SQL. It selects <strong>nothing about the
/// Owner's CV</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
public interface IWeeklyPrecisionQuery
{
    /// <summary>
    /// The <see cref="WeeklyPrecision"/> of the most recently opened rating round, or <c>null</c> when no round
    /// has ever been opened. A week whose delivered top-ten drew no ratings yields a precision of zero, not
    /// <c>null</c> — zero is a measured value; <c>null</c> means "not yet measured".
    /// </summary>
    Task<WeeklyPrecision?> LatestAsync(CancellationToken cancellationToken = default);
}
