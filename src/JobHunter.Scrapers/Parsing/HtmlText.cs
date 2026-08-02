using System.Net;
using System.Text;

namespace JobHunter.Scrapers.Parsing;

/// <summary>
/// Turns a provider's HTML (or HTML-escaped HTML) description into the plain text the content hash and,
/// later, the model see. Greenhouse's <c>content</c> is HTML-escaped HTML, so it is decoded twice before
/// the tags are stripped (contract §Greenhouse). Decoding is idempotent on already-plain text, so the
/// second pass is harmless for providers that escape only once. Tag stripping and whitespace collapsing
/// are a single char scan — no regex, so nothing here is generated code that would dodge the coverage gate.
/// </summary>
internal static class HtmlText
{
    /// <summary>Double-decodes HTML entities, strips tags, and collapses whitespace to single spaces.</summary>
    public static string ToPlainText(string? raw)
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
