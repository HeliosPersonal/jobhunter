using System.Net;
using System.Text;

namespace JobHunter.Application.Normalization;

/// <summary>
/// Reduces a provider's HTML (or HTML-escaped HTML) description to the plain text the fingerprint and,
/// later, the model see (data-model §jobs — "HTML stripped to plain text at the boundary"). It double-
/// decodes entities (Greenhouse escapes its HTML once inside JSON), strips tags with a single char scan —
/// no regex, so nothing here dodges the coverage gate — and collapses whitespace. Decoding is idempotent
/// on already-plain text, so the second pass is harmless for providers that escape only once. Pure: no
/// clock, no randomness, invariant behaviour, so reprocessing (QG-3) is free.
/// </summary>
public static class PlainText
{
    /// <summary>Double-decodes HTML entities, strips tags and collapses whitespace to single spaces.</summary>
    public static string FromHtml(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(WebUtility.HtmlDecode(raw));
        return CollapseWhitespace(StripTags(decoded));
    }

    private static string StripTags(string html)
    {
        var builder = new StringBuilder(html.Length);
        var insideTag = false;

        foreach (var c in html)
        {
            if (c == '<')
            {
                insideTag = true;
                builder.Append(' ');
            }
            else if (c == '>')
            {
                insideTag = false;
            }
            else if (!insideTag)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }
}
