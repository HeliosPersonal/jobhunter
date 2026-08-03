namespace JobHunter.Application.Enrichment;

/// <summary>
/// Tunables for the daily Run (F3 SAD §6.1, PRD §6). Bound and validated at startup
/// (coding-standards §options). The <see cref="CeilingUsd"/> is snapshotted onto every Run at creation
/// and immutable thereafter, so a configuration change mid-Run cannot retroactively authorise spend
/// (ADR-F3-0002); changing it only affects Runs created afterwards.
/// </summary>
public sealed class RunOptions
{
    public const string SectionName = "Run";

    /// <summary>The per-Run cost ceiling in USD, snapshotted onto each Run (PRD §6: $2.00 default).</summary>
    public decimal CeilingUsd { get; init; } = 2.00m;

    /// <summary>
    /// The look-back for the very first Run's <c>cutoff_from</c>, when no previous Run exists to inherit a
    /// <c>cutoff_to</c> from. Every later Run's window starts at the previous Run's <c>cutoff_to</c>, so a
    /// skipped day is caught up rather than lost (data-model §runs).
    /// </summary>
    public TimeSpan InitialLookBack { get; init; } = TimeSpan.FromHours(24);
}
