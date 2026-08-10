namespace JobHunter.Domain.Reporting;

/// <summary>
/// One delivered card in a week's top-ten (F4 T20, the weekly precision@10 rating loop). It is the previous
/// week's ranked, actually-<em>delivered</em> cards — the denominator the Owner is asked to rate "worth
/// opening?" and over which precision@10 is measured. Delivered means a matching row exists in the append-only
/// <c>delivery_log</c> (invariant 8): a card assembled but never sent is not part of the week the Owner saw.
///
/// <para>It carries only the identity needed to render a rating prompt and to capture the resulting
/// <c>Rated</c> signal — <strong>nothing about the Owner</strong>. The CV crosses exactly one boundary, and it
/// is not this one (F4 invariant).</para>
/// </summary>
/// <param name="JobId">The job the card is about — the key a <c>Rated</c> signal is captured against.</param>
/// <param name="RunId">The Run that produced the card, which with <see cref="JobId"/> reconstructs the card key.</param>
/// <param name="Rank">The 1-based presentation rank the card was shown at.</param>
public sealed record WeeklyTopCard(Guid JobId, Guid RunId, int Rank);
