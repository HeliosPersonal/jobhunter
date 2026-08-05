using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace JobHunter.Telegram.Formatting;

/// <summary>
/// The single path any dynamic value takes to a Telegram message (F5 message contract §Escaping). Every
/// character MarkdownV2 treats as markup — <c>_ * [ ] ( ) ~ ` &gt; # + - = | { } . !</c> — is
/// backslash-escaped, because one unescaped special silently fails the <em>whole</em> send, not just its
/// own token. This is the canonical escaper the F5 formatters build on; the F9 <c>/search</c> renderer
/// uses the same type, so there is exactly one implementation and the escape set cannot drift between the
/// two surfaces (the cross-feature decoupling decision, now collapsed).
///
/// <para>An architecture test (<c>ConventionRulesTests.Rule9</c>) forbids interpolating a non-constant
/// straight into message text anywhere under <c>Formatting</c>, so a formatter physically cannot bypass
/// this method — the only way a value reaches a card is through <see cref="Escape"/>.</para>
/// </summary>
internal static class MarkdownV2Escaper
{
    private static readonly FrozenSet<char> Special =
        new[] { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' }
            .ToFrozenSet();

    /// <summary>Returns <paramref name="value"/> with every MarkdownV2 special character backslash-escaped.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            if (Special.Contains(ch))
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Truncates <paramref name="value"/> to at most <paramref name="maxGraphemes"/> user-perceived
    /// characters, backing off to the last word boundary and appending an ellipsis. Truncation counts
    /// <em>graphemes</em>, not bytes or UTF-16 units, so a flag emoji, a combining mark or a CJK glyph at
    /// the boundary is never split into a broken half (the rendering-corpus rule). The returned value is
    /// still raw — the caller escapes it — so this composes with <see cref="Escape"/> rather than assuming
    /// a particular escaping.
    /// </summary>
    public static string Truncate(string? value, int maxGraphemes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxGraphemes);

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            elements.Add((string)enumerator.Current);
        }

        if (elements.Count <= maxGraphemes)
        {
            return value;
        }

        var clipped = string.Concat(elements.Take(maxGraphemes));
        var lastSpace = clipped.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            clipped = clipped[..lastSpace];
        }

        return clipped + "…";
    }

    /// <summary>
    /// Renders a whole-thousands money figure the way the digest header and cards do — <c>185000</c>
    /// becomes <c>"185k"</c>, and a sub-thousand amount is shown verbatim so a small stipend is not
    /// misread as a huge one. Invariant culture, because the digest is a single-Owner English artifact.
    /// </summary>
    public static string FormatThousands(int amount) =>
        amount >= 1000
            ? (amount / 1000).ToString("0", CultureInfo.InvariantCulture) + "k"
            : amount.ToString("0", CultureInfo.InvariantCulture);
}
