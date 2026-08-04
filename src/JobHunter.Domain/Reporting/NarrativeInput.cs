namespace JobHunter.Domain.Reporting;

/// <summary>
/// The aggregate facts a <see cref="Digest"/>'s market note is synthesised from (F5 SAD §6.1, T05). It is
/// deliberately <strong>only counts and one salary statistic</strong> — the numbers already destined for the
/// digest header and footer — so the narrative call carries nothing about the Owner. The CV crosses exactly
/// one boundary (F4's match prompt) and it is not this one: there is no CV text, no card reason, no job
/// description here, only what the Owner already sees at a glance.
/// </summary>
/// <param name="TotalNewJobs">New jobs in the Run's window (the header's "N new roles").</param>
/// <param name="StrongMatches">Shown scores at or above the card threshold.</param>
/// <param name="CardCount">Cards actually presented after selection and apply-link verification.</param>
/// <param name="AvgSalaryUsd">The header's average advertised USD salary, or null when too few carry one.</param>
/// <param name="SuppressedCount">Scores withheld with a reason (invariant 11).</param>
/// <param name="CarriedOverCount">Items whose batch missed the deadline (AC-06).</param>
/// <param name="DegradedSourceCount">Quarantined sources named in the footer.</param>
public sealed record NarrativeInput(
    int TotalNewJobs,
    int StrongMatches,
    int CardCount,
    decimal? AvgSalaryUsd,
    int SuppressedCount,
    int CarriedOverCount,
    int DegradedSourceCount)
{
    /// <summary>
    /// True when the day has anything a market note could illuminate. A Run with no new jobs and nothing
    /// shown has nothing to synthesise — the deterministic template says so for free, and no model call
    /// (and no spend) is made (ADR-F5-0001).
    /// </summary>
    public bool HasSomethingToSay => TotalNewJobs > 0 || CardCount > 0 || StrongMatches > 0;
}
