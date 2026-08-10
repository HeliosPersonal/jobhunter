namespace JobHunter.Domain.Reporting;

/// <summary>
/// One week's <c>precision@10</c> from the weekly rating loop (F4 T20 done-when 3, [[docs/DECISION-LOG|D5]]).
/// It is the binary, ratings-based measure the Owner produces directly: the share of the previous week's
/// top-ten <em>delivered</em> cards the Owner rated "worth opening". The target is ≥ 0.6 and improving after
/// preference learning, with a baseline captured at M4 — the empirical counterpart to the golden ranking set,
/// which proves the ranking is stable but not that it is good.
///
/// <para>Distinct from <see cref="PrecisionAtTenPoint"/>, which is the F7 <em>engagement</em>-based series over
/// <c>scores</c>: this one's numerator is the count of <c>Rated</c> signals on the week's delivered top-ten,
/// its denominator is that delivered count (at most ten). A "worth opening" tap is one <c>Rated</c> signal; not
/// tapping records nothing, so absence is the "not worth" answer. It carries <strong>nothing about the
/// Owner's CV</strong> — the CV crosses exactly one boundary, and it is not this one (F4 invariant).</para>
/// </summary>
/// <param name="WeekStart">The start of the half-open week <c>[WeekStart, WeekStart + 7d)</c> this measures.</param>
/// <param name="Considered">How many delivered top-ten cards the week is measured over — at most ten, and always positive.</param>
/// <param name="Hits">How many of the <see cref="Considered"/> cards the Owner rated "worth opening".</param>
/// <param name="Precision"><see cref="Hits"/> over <see cref="Considered"/> in [0, 1], the week's precision@10.</param>
public sealed record WeeklyPrecision(
    DateTimeOffset WeekStart,
    int Considered,
    int Hits,
    decimal Precision);
