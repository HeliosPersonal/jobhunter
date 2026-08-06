namespace JobHunter.Domain.Reporting;

/// <summary>
/// The Owner's engagement over one window (F5 T11 <c>/stats</c>): how many cards were delivered, and how the
/// Owner reacted — opened, ignored, saved — plus how many jobs were marked applied. Delivered comes from the
/// append-only <c>delivery_log</c> (invariant 8); the reactions and the applied count are <c>signals</c> of
/// the matching kinds (F5 writes the card actions, F6 the applied outcome; F7 owns the schema). It is a plain
/// count set — the week window, the precision and the week-over-week trend are computed in the command from
/// two of these, so the arithmetic stays unit-testable without a database.
///
/// <para>It carries <strong>nothing about the Owner</strong> beyond these counts — the CV crosses exactly one
/// boundary, and it is not this one (F4 invariant).</para>
/// </summary>
/// <param name="Delivered">Cards delivered in the window (distinct delivery-log rows).</param>
/// <param name="Opened">Cards whose apply link the Owner opened.</param>
/// <param name="Ignored">Cards the Owner dismissed without acting.</param>
/// <param name="Saved">Cards the Owner saved for later.</param>
/// <param name="Applied">Jobs the Owner marked applied.</param>
public sealed record WeeklyEngagement(int Delivered, int Opened, int Ignored, int Saved, int Applied)
{
    /// <summary>A window with no activity — the zero from which an empty week reads as "nothing yet".</summary>
    public static readonly WeeklyEngagement Empty = new(0, 0, 0, 0, 0);

    /// <summary>
    /// The share of delivered cards the Owner engaged with positively — opened, saved or applied, never
    /// ignored — as a 0–1 fraction, or null when nothing was delivered (a precision over zero cards is
    /// undefined, not zero). Capped at 1 so a card acted on more than one way never reads as over 100%.
    /// </summary>
    public decimal? Precision =>
        Delivered == 0 ? null : Math.Min(1m, (decimal)(Opened + Saved + Applied) / Delivered);
}
