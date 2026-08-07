using System.Net;
using System.Text;

namespace JobHunter.Scrapers.Parsing;

/// <summary>
/// Turns a fetched HTML page into the capped plain text the synthesiser sees (SAD §8 Extraction, T04
/// "Done when"). It is deliberately stricter than <see cref="HtmlText"/>, which only strips tags for a
/// provider's already-clean job description: a research page is an arbitrary public site, so
/// <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> bodies (and HTML comments) are discarded with their
/// content — not left behind as JavaScript or CSS text the model would then try to summarise. Block-level
/// tags become paragraph breaks so the cap can fall on a paragraph boundary rather than mid-sentence, and
/// a page with no extractable text is the empty string — there is no headless browser, so a page that
/// renders its text with JavaScript is simply no document (T04 "Done when").
///
/// <para>All scanning is a single char pass — no regex, so nothing here is generated code that would dodge
/// the coverage gate, matching the convention in <see cref="HtmlText"/> and <see cref="JsonLdExtractor"/>.</para>
/// </summary>
internal static class ResearchContentExtractor
{
    /// <summary>The per-document character cap (SAD §8 Extraction). One document never exceeds this.</summary>
    public const int MaxChars = 20_000;

    // Tags whose presence ends a paragraph — a block boundary. Everything else (inline markup like <b>,
    // <a>, <span>) is dropped without splitting the text, so a sentence with inline emphasis stays whole.
    private static readonly HashSet<string> BlockTags =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "p", "div", "br", "li", "ul", "ol", "tr", "table", "section", "article", "header",
            "footer", "nav", "aside", "main", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote",
            "pre", "figure", "figcaption", "hr", "dd", "dt", "dl",
        };

    /// <summary>
    /// Extracts the plain text of <paramref name="html"/>, capped at <paramref name="maxChars"/> (default
    /// <see cref="MaxChars"/>) on a paragraph boundary where possible, else a word boundary. Returns the
    /// empty string — never null — when there is no extractable text.
    /// </summary>
    public static string ToPlainText(string? html, int maxChars = MaxChars)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var paragraphs = ExtractParagraphs(html);
        return Cap(paragraphs, maxChars);
    }

    // Walk the markup once, dropping script/style/comment regions wholesale and collecting each block's
    // text as a separate paragraph. Inline tags are treated as nothing (removed), so their surrounding
    // text joins into one paragraph.
    private static List<string> ExtractParagraphs(string html)
    {
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        var index = 0;

        void FlushParagraph()
        {
            var text = Normalise(current.ToString());
            if (text.Length > 0)
            {
                paragraphs.Add(text);
            }

            current.Clear();
        }

        while (index < html.Length)
        {
            var c = html[index];
            if (c != '<')
            {
                current.Append(c);
                index++;
                continue;
            }

            // An HTML comment: skip to its terminator, content and all.
            if (Matches(html, index, "<!--"))
            {
                index = SkipTo(html, index + 4, "-->", "-->".Length);
                continue;
            }

            // A script or style element: skip the whole element including its body.
            if (TryMatchRawElement(html, index, "script", out var afterScript))
            {
                index = afterScript;
                continue;
            }

            if (TryMatchRawElement(html, index, "style", out var afterStyle))
            {
                index = afterStyle;
                continue;
            }

            var tagEnd = html.IndexOf('>', index);
            if (tagEnd < 0)
            {
                // An unterminated tag at the end of the document — treat the rest as gone.
                break;
            }

            var name = TagName(html, index, tagEnd);
            if (BlockTags.Contains(name))
            {
                FlushParagraph();
            }
            else
            {
                // Inline tag: a word boundary so "a<b>b</b>" does not fuse into "ab".
                current.Append(' ');
            }

            index = tagEnd + 1;
        }

        FlushParagraph();
        return paragraphs;
    }

    // Decode entities and collapse runs of whitespace to single spaces within one paragraph.
    private static string Normalise(string raw)
    {
        var decoded = WebUtility.HtmlDecode(raw);
        var builder = new StringBuilder(decoded.Length);
        var pendingSpace = false;

        foreach (var c in decoded)
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

    // Join paragraphs with a blank line, accumulating only while the whole result stays within the cap.
    // A single paragraph larger than the cap is truncated on a word boundary — the only case where the
    // cut is not a paragraph boundary.
    private static string Cap(List<string> paragraphs, int maxChars)
    {
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        const string separator = "\n\n";
        var builder = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            var addition = builder.Length == 0 ? paragraph : separator + paragraph;
            if (builder.Length + addition.Length <= maxChars)
            {
                builder.Append(addition);
                continue;
            }

            if (builder.Length > 0)
            {
                // We already have whole paragraphs — stop on this paragraph boundary rather than cut into it.
                break;
            }

            // The first paragraph alone exceeds the cap: truncate it on a word boundary.
            return TruncateOnWord(paragraph, maxChars);
        }

        return builder.ToString();
    }

    private static string TruncateOnWord(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var slice = text[..maxChars];
        var lastSpace = slice.LastIndexOf(' ');
        var cut = lastSpace > 0 ? slice[..lastSpace] : slice;
        return cut.TrimEnd();
    }

    // --- small scanning helpers, each a plain char comparison ---

    private static bool Matches(string html, int at, string token) =>
        at + token.Length <= html.Length
        && html.AsSpan(at, token.Length).Equals(token, StringComparison.OrdinalIgnoreCase);

    // Advance past the next occurrence of terminator (inclusive); if absent, jump to the end.
    private static int SkipTo(string html, int from, string terminator, int terminatorLength)
    {
        var found = html.IndexOf(terminator, from, StringComparison.OrdinalIgnoreCase);
        return found < 0 ? html.Length : found + terminatorLength;
    }

    // A raw-text element (script/style): matched as "<name" followed by a delimiter, skipped through its
    // matching "</name>" so its body never contributes text. Returns false when this is not that element.
    private static bool TryMatchRawElement(string html, int at, string name, out int afterElement)
    {
        afterElement = at;
        var open = "<" + name;
        if (!Matches(html, at, open))
        {
            return false;
        }

        // The char after the name must delimit a tag (space, '>', '/') so "<sciencey>" is not "<sci"+.
        var afterName = at + open.Length;
        if (afterName < html.Length)
        {
            var next = html[afterName];
            if (next is not (' ' or '\t' or '\r' or '\n' or '>' or '/'))
            {
                return false;
            }
        }

        var openEnd = html.IndexOf('>', at);
        if (openEnd < 0)
        {
            afterElement = html.Length;
            return true;
        }

        // A self-closing script/style (rare) ends at its own '>'.
        if (openEnd > at && html[openEnd - 1] == '/')
        {
            afterElement = openEnd + 1;
            return true;
        }

        var close = "</" + name + ">";
        afterElement = SkipTo(html, openEnd + 1, close, close.Length);
        return true;
    }

    // The tag name of a tag starting at '<': the run of name chars after '<' and an optional '/'.
    private static string TagName(string html, int tagStart, int tagEnd)
    {
        var i = tagStart + 1;
        if (i < tagEnd && html[i] == '/')
        {
            i++;
        }

        var start = i;
        while (i < tagEnd && (char.IsLetterOrDigit(html[i]) || html[i] == '-'))
        {
            i++;
        }

        return html[start..i];
    }
}
