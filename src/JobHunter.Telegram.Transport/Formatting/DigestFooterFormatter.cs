using System.Globalization;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// Renders a <see cref="FooterView"/> as the digest's footer message (F5 message contract §Footer). The
/// footer is what makes [[DECISION-LOG|D7]] and invariant 11 visible — the hidden breakdown proves a
/// suppressed job was filtered for a stated reason, not lost. It renders <strong>only when it has
/// something to say</strong>: <see cref="Format"/> returns null for an empty footer so the caller sends no
/// message, and each of the three lines is omitted when its own count is zero.
///
/// <para>Every dynamic value passes through <see cref="MarkdownV2Escaper.Escape"/>. The divider and the
/// warning glyph are the only literals.</para>
/// </summary>
internal static class DigestFooterFormatter
{
    private const string Divider = "─────────────";

    /// <summary>Renders the footer, or null when there is nothing to show.</summary>
    public static string? Format(FooterView footer)
    {
        ArgumentNullException.ThrowIfNull(footer);

        if (!footer.HasContent)
        {
            return null;
        }

        var lines = new List<string> { Divider };

        if (footer.HiddenCount > 0 && footer.HiddenBreakdown.Count > 0)
        {
            lines.Add(MarkdownV2Escaper.Escape(BuildHiddenLine(footer)));
        }

        if (footer.StillProcessingCount > 0)
        {
            var count = footer.StillProcessingCount.ToString("0", CultureInfo.InvariantCulture);
            lines.Add(MarkdownV2Escaper.Escape(
                $"{count} jobs still processing — they'll be in tomorrow's digest"));
        }

        foreach (var source in footer.DegradedSources.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            lines.Add("⚠️ " + MarkdownV2Escaper.Escape($"1 source degraded: {source.Trim()} (quarantined)"));
        }

        if (!footer.LearningEnabled)
        {
            // AC-07: the daily summary states the ordering was shaped by explicit preferences alone.
            lines.Add(MarkdownV2Escaper.Escape("Preference learning is off — ranked on explicit preferences only"));
        }

        return string.Join("\n", lines);
    }

    private static string BuildHiddenLine(FooterView footer)
    {
        var total = footer.HiddenCount.ToString("0", CultureInfo.InvariantCulture);
        var parts = footer.HiddenBreakdown
            .Where(t => !string.IsNullOrWhiteSpace(t.Reason))
            .Select(t => $"{t.Count.ToString("0", CultureInfo.InvariantCulture)} {t.Reason.Trim()}");

        return $"{total} hidden: " + string.Join(" · ", parts);
    }
}
