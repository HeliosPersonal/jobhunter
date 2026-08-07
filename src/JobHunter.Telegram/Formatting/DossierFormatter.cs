using System.Globalization;
using System.Text;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// Renders one <see cref="DossierView"/> as a company research message (F8 T09, research-schema contract).
/// The layout is fixed: a bold company header with the dossier's age, the summary, then the claims — warnings
/// first (the aggregate's own order, AC-04) — each as a bullet whose text links to its source (AC-02) and
/// carries the date it was observed (AC-03), and finally the categories that produced nothing, stated plainly
/// so absence of information is itself information (AC-07).
///
/// <para>Every dynamic value passes through <see cref="MarkdownV2Escaper"/> — the claim text and company name
/// through <see cref="MarkdownV2Escaper.Escape"/>, the source citation through
/// <see cref="MarkdownV2Escaper.Link"/> which escapes both label and destination — so a claim full of
/// <c>*markup*</c> or a URL containing a <c>)</c> renders literally and can never break the send, which
/// <c>ConventionRulesTests.Rule9</c> enforces. The dossier carries <strong>nothing about the Owner</strong>:
/// only public company facts and the URLs they came from (the CV crosses exactly one boundary, not this one).</para>
/// </summary>
internal static class DossierFormatter
{
    public static string Format(DossierView dossier)
    {
        ArgumentNullException.ThrowIfNull(dossier);

        var builder = new StringBuilder();

        // Header — bold company name, then the dossier's age so a stale dossier is never mistaken for fresh.
        builder.Append('*')
            .Append(MarkdownV2Escaper.Escape(dossier.CompanyName))
            .Append("*\n");
        builder.Append('_')
            .Append(MarkdownV2Escaper.Escape("Researched " + FormatDate(dossier.GeneratedAt)))
            .Append("_\n\n");

        if (!string.IsNullOrWhiteSpace(dossier.Summary))
        {
            builder.Append(MarkdownV2Escaper.Escape(dossier.Summary.Trim())).Append("\n\n");
        }

        // Claims in the order handed over — the aggregate already put warnings first (AC-04).
        foreach (var claim in dossier.Claims)
        {
            builder.Append(RenderClaim(claim)).Append('\n');
        }

        // Categories with no documents — named, not omitted, so the Owner knows they were checked (AC-07).
        var unavailable = dossier.CategoriesUnavailable
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList();
        if (unavailable.Count > 0)
        {
            builder.Append('\n')
                .Append(MarkdownV2Escaper.Escape("No information found: " + string.Join(", ", unavailable)));
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string RenderClaim(DossierClaim claim)
    {
        // Bullet · [warning glyph] · claim text · a dated source link. The claim text is escaped; the source
        // is a MarkdownV2 link whose label ("source") and destination are both escaped for their own context.
        var glyph = claim.IsWarning ? "⚠️ " : string.Empty;
        var citation = MarkdownV2Escaper.Link(FormatDate(claim.ObservedAt), claim.SourceUrl);

        return "• " + glyph
            + MarkdownV2Escaper.Escape(claim.Claim.Trim())
            + " (" + citation + ")";
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
