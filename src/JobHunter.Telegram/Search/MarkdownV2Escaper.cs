using System.Collections.Frozen;

namespace JobHunter.Telegram.Search;

/// <summary>
/// Escapes a dynamic value for Telegram MarkdownV2 (F5 message contract §Escaping). Every character
/// MarkdownV2 treats as markup — <c>_ * [ ] ( ) ~ ` &gt; # + - = | { } . !</c> — is backslash-escaped,
/// because a single unescaped one silently fails the whole send. This is the F9 <c>/search</c> command's
/// own minimal escaper: F5 owns the full formatter and F10 the command registry, and neither is merged, so
/// the search command cannot depend on them (the cross-feature decoupling decision). When F5's
/// <c>MarkdownV2Escaper</c> lands this collapses to a single shared implementation, but the escape set is
/// fixed by Telegram, so the two agree by construction.
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

        var builder = new System.Text.StringBuilder(value.Length + 8);
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
}
