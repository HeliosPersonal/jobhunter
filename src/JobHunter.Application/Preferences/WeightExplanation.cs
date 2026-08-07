using System.Globalization;
using JobHunter.Domain.Preferences;

namespace JobHunter.Application.Preferences;

/// <summary>
/// Renders a learned <see cref="PreferenceWeight"/> as the one plain sentence AC-03 / QG-1 require — the
/// "34 of your last 38 ignores were below 170k EUR" of
/// [[adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]]. This is the whole of the explainability
/// contract at the display boundary: a weight the Owner can read in a sentence is a weight they can question
/// and switch off, and a filter they cannot read is indistinguishable from a bug ([[DECISION-LOG|D7]]).
///
/// <para>The count is derived from the <em>stored</em> evidence — <see cref="PreferenceWeight.PositiveRate"/>
/// and <see cref="PreferenceWeight.SupportingSignalCount"/> — never recomputed, so the sentence stays stable
/// after the fitting window has moved on. The rate also fixes the direction: below half the Owner reacted
/// against the value (they "passed on" it), at or above half they leant in (they "engaged with" it); the
/// boundary belongs to engagement because an evenly-reacted value earns no weight at all, so a weight being
/// explained at exactly half is one that leant in.</para>
///
/// <para>Pure and side-effect free, and it emits <em>plain</em> text: the Telegram layer escapes it for
/// MarkdownV2, the API returns it verbatim. It lives in Application because both surfaces consume it and it
/// composes only domain types.</para>
/// </summary>
public static class WeightExplanation
{
    /// <summary>
    /// The one-sentence explanation of <paramref name="weight"/>, quoting the count and total of the reaction
    /// that produced it in natural language.
    /// </summary>
    public static string Describe(PreferenceWeight weight)
    {
        ArgumentNullException.ThrowIfNull(weight);

        var total = weight.SupportingSignalCount;
        var engaged = weight.PositiveRate >= 0.5m;

        // Quote the dominant reaction's share of the evidence, rounded to a whole, honest count of signals.
        var share = engaged ? weight.PositiveRate : 1m - weight.PositiveRate;
        var count = (int)Math.Round(share * total, MidpointRounding.AwayFromZero);

        var verb = engaged ? "engaged with" : "passed on";
        var subject = SubjectOf(weight.Dimension, weight.Value);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"You {verb} {count} of the last {total} {subject}.");
    }

    /// <summary>
    /// Names a <c>(dimension, value)</c> in the same natural language the digest uses, so the sentence reads
    /// as English rather than as a key. An unmapped dimension falls back to naming the value plainly rather
    /// than throwing — a new dimension should degrade to a readable sentence, not an error at the boundary.
    /// </summary>
    private static string SubjectOf(Dimension dimension, string value) => dimension switch
    {
        Dimension.SalaryBand => $"roles in the {value} salary band",
        Dimension.Country => $"roles in {value}",
        Dimension.CompanySize => $"roles at {value} companies",
        Dimension.Technology => $"roles using {value}",
        Dimension.TimezoneBand => $"roles in the {value} timezone",
        Dimension.RemotePolicy => $"{value.ToLowerInvariant()} roles",
        Dimension.EmploymentType => $"{value} roles",
        Dimension.AiUsage => $"roles with {value} AI usage",
        Dimension.RoleFamily => $"{value} roles",
        _ => $"{value} roles",
    };
}
