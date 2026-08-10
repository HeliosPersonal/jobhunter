using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port behind the weekly precision@10 rating loop (F4 T20). It returns the previous week's top-ten
/// <em>delivered</em> cards — the ones the Owner actually saw, ranked as shown — which are both the prompt the
/// weekly job renders ("was this worth opening?") and the denominator precision@10 is measured over. A card is
/// delivered when a row exists for it in the append-only <c>delivery_log</c> (invariant 8); an assembled card
/// that was never sent is not part of the week under review.
///
/// <para>Read-only (Dapper, architecture rule 4); defined in Domain so the handler depends on the port, not
/// the query. It selects <strong>nothing about the Owner</strong> — the CV crosses exactly one boundary, and
/// it is not this one (F4 invariant).</para>
/// </summary>
public interface IWeeklyTopCardsQuery
{
    /// <summary>
    /// The top-ten cards delivered in the half-open window <c>[from, to)</c>, ordered by rank ascending. The
    /// window is half-open so two adjacent weeks never both claim a boundary delivery. At most ten rows: the
    /// precision@10 denominator is capped at ten even on a week that delivered more.
    /// </summary>
    Task<IReadOnlyList<WeeklyTopCard>> TopCardsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
