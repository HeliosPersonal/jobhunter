using System.Globalization;
using System.Text;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// Renders a <see cref="HeaderView"/> as the digest's header message — the three-second message the whole
/// product is judged by (F5 message contract §Header, §Degraded-day variants). Six content lines maximum
/// in every variant (AC-01), which this formatter guarantees structurally: each variant emits a fixed,
/// small set of lines, so a header can never grow past the fold. Every degraded day still renders a header
/// — a partial or budget-capped run produces a different message, never a missing one (ADR-F5-0001).
///
/// <para>Every dynamic value passes through <see cref="MarkdownV2Escaper.Escape"/>; the only literals are
/// the markup asterisks, the fixed prose and the emoji. The header states only counts, one salary figure
/// and the single best opportunity — all of which the Owner already sees at a glance, nothing about the
/// Owner (the CV crosses exactly one boundary, and it is not this one).</para>
/// </summary>
internal static class DigestHeaderFormatter
{
    /// <summary>AC-01: the header is at most six content (non-blank) lines in every variant.</summary>
    public const int MaxContentLines = 6;

    private const string Greeting = "🌅 *Good morning\\.*";

    public static string Format(HeaderView header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return header.Mode switch
        {
            DigestMode.NothingNew => FormatNothingNew(header),
            DigestMode.Partial => FormatPartial(header),
            DigestMode.BudgetReached => FormatBudgetReached(header),
            _ => FormatFull(header),
        };
    }

    private static string FormatFull(HeaderView header)
    {
        var blocks = new List<string> { Greeting, BuildCounts(header) };

        if (header.TopOpportunity is { } best)
        {
            blocks.Add(BuildOpportunity(best));
        }

        blocks.Add(Italic(BuildHiddenLine(header)));
        return string.Join("\n\n", blocks);
    }

    private static string FormatNothingNew(HeaderView header)
    {
        var checkedLine = "No new roles today. "
            + header.CompaniesChecked.ToString("0", CultureInfo.InvariantCulture)
            + " companies checked, nothing matched.";

        return string.Join(
            "\n\n",
            Greeting,
            MarkdownV2Escaper.Escape(checkedLine),
            Italic("This is normal. Everything is working."));
    }

    private static string FormatPartial(HeaderView header)
    {
        var still = header.StillAnalysing.ToString("0", CultureInfo.InvariantCulture);

        return string.Join(
            "\n\n",
            Greeting + " " + MarkdownV2Escaper.Escape("(partial)"),
            BuildCounts(header),
            Italic($"{still} roles are still being analysed. They'll appear tomorrow."));
    }

    private static string FormatBudgetReached(HeaderView header)
    {
        var total = header.TotalNewJobs.ToString("0", CultureInfo.InvariantCulture);
        var analysed = header.AnalysedCount.ToString("0", CultureInfo.InvariantCulture);

        var counts = "*" + MarkdownV2Escaper.Escape(total) + "* new · *"
            + MarkdownV2Escaper.Escape(analysed) + "* "
            + MarkdownV2Escaper.Escape("analysed before the daily budget was reached");

        return string.Join(
            "\n\n",
            Greeting + " " + MarkdownV2Escaper.Escape("(reduced)"),
            counts,
            Italic("Raise the ceiling or reduce the company list. Nothing was lost."));
    }

    private static string BuildCounts(HeaderView header)
    {
        var total = header.TotalNewJobs.ToString("0", CultureInfo.InvariantCulture);
        var strong = header.StrongMatches.ToString("0", CultureInfo.InvariantCulture);

        var counts = new StringBuilder();
        counts.Append('*').Append(MarkdownV2Escaper.Escape(total)).Append("* new · *")
            .Append(MarkdownV2Escaper.Escape(strong)).Append("* strong matches");

        if (header.AvgSalaryUsdThousands is { } avg)
        {
            var avgText = avg.ToString("0", CultureInfo.InvariantCulture) + "k USD";
            counts.Append(" · avg *").Append(MarkdownV2Escaper.Escape(avgText)).Append('*');
        }

        return counts.ToString();
    }

    private static string BuildOpportunity(HeaderOpportunity best)
    {
        var title = MarkdownV2Escaper.Truncate(best.Title, CardFormatter.MaxTitleGraphemes);
        var score = Math.Round(best.Score, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        var line = new StringBuilder();
        line.Append("🏆 *").Append(MarkdownV2Escaper.Escape(title)).Append("* — ")
            .Append(MarkdownV2Escaper.Escape(best.Company)).Append(" · *")
            .Append(MarkdownV2Escaper.Escape(score)).Append('*');

        var highlights = best.Highlights.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
        if (highlights.Count > 0)
        {
            line.Append("\n   ").Append(MarkdownV2Escaper.Escape(string.Join(" · ", highlights)));
        }

        return line.ToString();
    }

    private static string BuildHiddenLine(HeaderView header)
    {
        var cards = header.CardCount.ToString("0", CultureInfo.InvariantCulture);
        var sentence = new StringBuilder($"{cards} cards below.");

        if (header.HiddenCount > 0)
        {
            sentence.Append(' ').Append(header.HiddenCount.ToString("0", CultureInfo.InvariantCulture))
                .Append(" hidden");

            var reasons = header.HiddenReasons.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            if (reasons.Count > 0)
            {
                sentence.Append(" (").Append(string.Join(", ", reasons)).Append(')');
            }

            sentence.Append('.');
        }

        return sentence.ToString();
    }

    // Wraps an entire plain sentence in a MarkdownV2 italic run: the whole sentence is escaped, then the
    // underscores are added as literal markup around it.
    private static string Italic(string plain) => "_" + MarkdownV2Escaper.Escape(plain) + "_";
}
