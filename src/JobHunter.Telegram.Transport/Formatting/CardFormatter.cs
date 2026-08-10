using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// Renders one <see cref="CardView"/> as a card message (F5 message contract §Card). The layout is fixed
/// so the Owner reads it by muscle memory: bold title (truncated to 60 graphemes at a word boundary),
/// company · stage · location, an optional salary-and-score line, then exactly three reasons. Every dynamic
/// value passes through <see cref="MarkdownV2Escaper.Escape"/> — there is no interpolation of a raw value
/// into the text, which an architecture test enforces — so a title full of <c>*markup*</c> renders
/// literally and can never break the send.
///
/// <para>The card carries <strong>nothing about the Owner</strong>: title, company and reasons are the
/// job's and the ranking's, never the CV (the CV crosses exactly one boundary, and it is not this one). The
/// four action buttons are an inline keyboard built at delivery (T10), not message text, so they are not
/// part of what this formatter produces.</para>
/// </summary>
internal static class CardFormatter
{
    /// <summary>Titles are truncated to 60 user-perceived characters (F5 message contract §Card).</summary>
    public const int MaxTitleGraphemes = 60;

    /// <summary>Exactly three reasons are shown — the ranking's own explanation, not a summary.</summary>
    public const int MaxReasons = 3;

    /// <summary>Each reason is capped at 90 graphemes (F5 message contract §Card).</summary>
    public const int MaxReasonGraphemes = 90;

    private const string CompanyStageUnknown = "Unknown";

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Format(CardView card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var builder = new StringBuilder();

        // Title — bold, truncated on graphemes so an emoji or CJK glyph at the boundary is never split.
        builder.Append('*')
            .Append(MarkdownV2Escaper.Escape(MarkdownV2Escaper.Truncate(card.Title, MaxTitleGraphemes)))
            .Append("*\n");

        // Company · stage · location. The stage is omitted when unknown, never shown as the literal "Unknown".
        builder.Append(MarkdownV2Escaper.Escape(BuildCompanyLine(card))).Append('\n');

        var salaryScoreLine = BuildSalaryScoreLine(card);
        if (salaryScoreLine is not null)
        {
            builder.Append(salaryScoreLine).Append('\n');
        }

        // A blank line separates the header block from the reasons, matching the contract layout.
        builder.Append('\n');

        foreach (var reason in card.Reasons.Where(r => !string.IsNullOrWhiteSpace(r)).Take(MaxReasons))
        {
            builder.Append("• ")
                .Append(MarkdownV2Escaper.Escape(NormaliseReason(reason)))
                .Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string BuildCompanyLine(CardView card)
    {
        var parts = new List<string> { card.Company };

        if (!string.IsNullOrWhiteSpace(card.Stage)
            && !string.Equals(card.Stage.Trim(), CompanyStageUnknown, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(card.Stage.Trim());
        }

        parts.Add(card.Location);
        return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string? BuildSalaryScoreLine(CardView card)
    {
        var salary = card.Salary is { } s ? BuildSalaryText(s) : null;
        var score = FormatScore(card.Score);

        if (salary is null)
        {
            // A card always shows its score; only the salary half is conditional.
            return "🎯 *" + MarkdownV2Escaper.Escape(score) + '*';
        }

        return "💰 " + MarkdownV2Escaper.Escape(salary) + " · 🎯 *" + MarkdownV2Escaper.Escape(score) + '*';
    }

    private static string BuildSalaryText(CardSalary salary)
    {
        var range = FormatRange(salary.Min, salary.Max);

        var text = new StringBuilder(range);
        if (!string.IsNullOrWhiteSpace(salary.Currency))
        {
            text.Append(' ').Append(salary.Currency.Trim());
        }

        // An estimate is never presented as fact — it is marked (est) with its confidence band.
        if (salary.IsEstimate)
        {
            text.Append(" (est");
            if (!string.IsNullOrWhiteSpace(salary.Confidence))
            {
                text.Append(", ").Append(salary.Confidence.Trim());
            }

            text.Append(')');
        }

        return text.ToString();
    }

    private static string FormatRange(int min, int max) =>
        min >= 1000 && max >= 1000
            ? (min / 1000).ToString("0", CultureInfo.InvariantCulture)
                + "–" + (max / 1000).ToString("0", CultureInfo.InvariantCulture) + "k"
            : MarkdownV2Escaper.FormatThousands(min) + "–" + MarkdownV2Escaper.FormatThousands(max);

    private static string NormaliseReason(string reason)
    {
        // A reason may arrive with newlines (a "\n\n" from a job board); collapse all whitespace to a
        // single space so the layout stays intact, then cap the length on graphemes.
        var collapsed = Whitespace.Replace(reason, " ").Trim();
        return MarkdownV2Escaper.Truncate(collapsed, MaxReasonGraphemes);
    }

    private static string FormatScore(decimal score) =>
        Math.Round(score, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}
