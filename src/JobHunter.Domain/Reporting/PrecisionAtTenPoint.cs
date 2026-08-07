namespace JobHunter.Domain.Reporting;

/// <summary>
/// One Run's <c>precision@10</c> — the share of the ten highest-scoring jobs it <em>showed</em> that the
/// Owner then engaged with (F7 T09 done-when 4, AC-08). It is the measure that answers whether preference
/// learning was worth building: a series of these points, split by whether the Run's scores were produced
/// before or after a learned model was active, is directly comparable — if the "after" points sit above the
/// "before" ones, the loop earned its place.
///
/// <para>Computed entirely from <em>recorded</em> data — the suppressed-nothing top-10 of a Run joined to the
/// positive signals on those jobs — so it is reproducible after the fact and never depends on live state. It
/// carries <strong>nothing about the Owner's CV</strong> (the CV crosses exactly one boundary, not this one).
/// A Run that showed nothing produces no point; <see cref="Considered"/> is therefore always positive.</para>
/// </summary>
/// <param name="RunId">The Run whose shown top-10 this measures.</param>
/// <param name="RunStartedAt">When the Run began — the x-axis of the before/after series.</param>
/// <param name="AfterActivation">
/// True when the Run's shown scores were produced with a learned model active (a non-null
/// <c>preference_model_id</c>); false when they predate activation. This is the bucket the comparison splits on.
/// </param>
/// <param name="Considered">How many shown jobs were weighed — at most ten, fewer on a thin day.</param>
/// <param name="Hits">How many of the <see cref="Considered"/> jobs drew a positive reaction from the Owner.</param>
/// <param name="Precision"><see cref="Hits"/> over <see cref="Considered"/> in [0, 1], the point's precision@10.</param>
public sealed record PrecisionAtTenPoint(
    Guid RunId,
    DateTimeOffset RunStartedAt,
    bool AfterActivation,
    int Considered,
    int Hits,
    decimal Precision);
