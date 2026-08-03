using System.Text;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Turns a published job title into its comparison form and extracts the seniority (T02, SAD §8). The
/// published title is never touched — this produces a <em>second</em> value used only for the
/// fingerprint (AC-05). The pipeline is deliberately limited to transformations that are certainly
/// meaning-preserving (ADR-F2-0001): lowercase, remove bracketed decoration, drop the location/remote
/// decoration that follows a dash separator, canonicalise seniority abbreviations to a single spelling,
/// and collapse whitespace. Anything that might carry meaning — a team after a pipe, a specialisation —
/// is kept, so it distinguishes rather than merges. No clock, no randomness, and only culture-invariant
/// casing and ordinal comparison, so the same title normalises identically on every machine and culture.
/// </summary>
public static class TitleNormalizer
{
    // Seniority aliases → (canonical spelling used in the normalised title, extracted level). Compared
    // against whitespace-split tokens with any trailing dot removed, so "sr." matches "sr". Ordered so a
    // deterministic first-match wins when a title carries more than one marker.
    private static readonly (string Alias, string Canonical, Seniority Level)[] SeniorityAliases =
    [
        ("principal", "principal", Seniority.Principal),
        ("staff", "staff", Seniority.Staff),
        ("senior", "senior", Seniority.Senior),
        ("snr", "senior", Seniority.Senior),
        ("sr", "senior", Seniority.Senior),
        ("iii", "senior", Seniority.Senior),
        ("manager", "manager", Seniority.Manager),
        ("mgr", "manager", Seniority.Manager),
        ("lead", "lead", Seniority.Lead),
        ("junior", "junior", Seniority.Junior),
        ("jnr", "junior", Seniority.Junior),
        ("jr", "junior", Seniority.Junior),
        ("graduate", "junior", Seniority.Junior),
        ("grad", "junior", Seniority.Junior),
        ("intermediate", "mid", Seniority.Mid),
        ("mid-level", "mid", Seniority.Mid),
        ("midlevel", "mid", Seniority.Mid),
        ("mid", "mid", Seniority.Mid),
        ("ii", "mid", Seniority.Mid),
    ];

    private static readonly char[] DashSeparators = ['-', '–', '—'];

    /// <summary>
    /// Normalises <paramref name="publishedTitle"/> and extracts its seniority. Returns an empty value
    /// with no seniority for a null or whitespace title — a missing title is caught upstream as a
    /// normalisation failure, not here.
    /// </summary>
    public static NormalizedTitle Normalize(string? publishedTitle)
    {
        if (string.IsNullOrWhiteSpace(publishedTitle))
        {
            return new NormalizedTitle(string.Empty, null);
        }

        var lowered = publishedTitle.ToLowerInvariant();
        var withoutBrackets = RemoveBracketedDecoration(lowered);
        var beforeDash = CutAtDashSeparator(withoutBrackets);
        var pipesToSpaces = beforeDash.Replace('|', ' ');

        Seniority? seniority = null;
        var tokens = pipesToSpaces.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var builder = new StringBuilder(pipesToSpaces.Length);

        foreach (var token in tokens)
        {
            var match = MatchSeniority(token);
            if (match is not null)
            {
                seniority ??= match.Value.Level;
                Append(builder, match.Value.Canonical);
            }
            else
            {
                Append(builder, token);
            }
        }

        return new NormalizedTitle(builder.ToString(), seniority);
    }

    private static (string Canonical, Seniority Level)? MatchSeniority(string token)
    {
        var candidate = token.TrimEnd('.');

        foreach (var (alias, canonical, level) in SeniorityAliases)
        {
            if (string.Equals(candidate, alias, StringComparison.Ordinal))
            {
                return (canonical, level);
            }
        }

        return null;
    }

    private static string RemoveBracketedDecoration(string value)
    {
        var builder = new StringBuilder(value.Length);
        var depth = 0;

        foreach (var c in value)
        {
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                // Never let an unmatched closing bracket drive depth negative.
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (depth == 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string CutAtDashSeparator(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (Array.IndexOf(DashSeparators, value[i]) < 0)
            {
                continue;
            }

            var spaceBefore = i > 0 && char.IsWhiteSpace(value[i - 1]);
            var spaceAfter = i + 1 < value.Length && char.IsWhiteSpace(value[i + 1]);
            if (spaceBefore || spaceAfter)
            {
                return value[..i];
            }
        }

        return value;
    }

    private static void Append(StringBuilder builder, string token)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(token);
    }
}
