using System.Net;

namespace JobHunter.Scrapers.Parsing;

/// <summary>
/// Reads a page's <c>&lt;title&gt;</c> for presentation only — a document's title is never a citation
/// (that is its URL), so a page without one is simply an empty title, not a failure. A single
/// case-insensitive char scan, no regex, matching the convention in <see cref="JsonLdExtractor"/>.
/// </summary>
internal static class PageTitle
{
    private const string Open = "<title";
    private const string Close = "</title>";

    /// <summary>The decoded, whitespace-trimmed contents of the first <c>&lt;title&gt;</c>, or empty.</summary>
    public static string From(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var open = html.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return string.Empty;
        }

        var contentStart = html.IndexOf('>', open);
        if (contentStart < 0)
        {
            return string.Empty;
        }

        var close = html.IndexOf(Close, contentStart, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return string.Empty;
        }

        var raw = html[(contentStart + 1)..close];
        return WebUtility.HtmlDecode(raw).Trim();
    }
}
