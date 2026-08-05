namespace JobHunter.Telegram.Formatting;

/// <summary>
/// The display projection of a <see cref="Domain.Reporting.Digest"/>'s footer (F5 message contract
/// §Footer). The footer only appears when it has something to say, and its second and third lines are
/// omitted when their count is zero — so a clean day ends on the last card, not on an empty divider. It
/// makes [[DECISION-LOG|D7]] visible: the suppression breakdown is what proves a hidden job was filtered
/// for a stated reason rather than lost to a bug (invariant 11).
/// </summary>
/// <param name="HiddenCount">Total scores suppressed; the first footer line is omitted when zero.</param>
/// <param name="HiddenBreakdown">The suppression reasons and their counts, in display order.</param>
/// <param name="StillProcessingCount">Items whose batch missed the deadline; the second line is omitted when zero.</param>
/// <param name="DegradedSources">Quarantined source names; the third line is omitted when empty.</param>
public sealed record FooterView(
    int HiddenCount,
    IReadOnlyList<FooterTally> HiddenBreakdown,
    int StillProcessingCount,
    IReadOnlyList<string> DegradedSources)
{
    /// <summary>True when at least one line would render — the footer is skipped entirely otherwise.</summary>
    public bool HasContent =>
        (HiddenCount > 0 && HiddenBreakdown.Count > 0)
        || StillProcessingCount > 0
        || DegradedSources.Count > 0;
}

/// <summary>One reason line of the footer's hidden breakdown — a count and the reason it hid (invariant 11).</summary>
/// <param name="Count">How many jobs this reason hid.</param>
/// <param name="Reason">The stated reason (e.g. "below salary floor").</param>
public sealed record FooterTally(int Count, string Reason);
