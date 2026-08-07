using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind <c>/hidden</c> (F7 T08, done-when 6, risk D3): the jobs the most recent Run suppressed,
/// each with the reason it was withheld. It makes suppression regret measurable — the Owner can see what the
/// learned model hid and, by opening one, reveal that it over-suppressed (invariant 11). Read-only (Dapper,
/// architecture rule 4); defined in Domain so the command handler depends on the port, not the SQL.
///
/// <para>Scoped to the latest Run only, so an old, since-superseded suppression does not linger; ordered
/// best-score first and capped at the caller's limit so a wide day never produces an unbounded message. It
/// selects <strong>nothing about the Owner's CV</strong> — the CV crosses exactly one boundary, not this one.</para>
/// </summary>
public interface IHiddenJobsQuery
{
    /// <summary>The latest Run's suppressed jobs with their reasons, best-score first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<HiddenJob>> HiddenAsync(int limit, CancellationToken cancellationToken = default);
}
