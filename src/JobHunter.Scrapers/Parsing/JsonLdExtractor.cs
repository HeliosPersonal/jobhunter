using System.Text.Json;

namespace JobHunter.Scrapers.Parsing;

/// <summary>
/// Pulls <c>schema.org/JobPosting</c> nodes out of a careers page. Two independent tolerances are the
/// whole point of Tier 2 (contract §Career pages): the page's <c>&lt;script type="application/ld+json"&gt;</c>
/// blocks are located by a plain char scan (no regex, so nothing here is generated code that dodges the
/// coverage gate), and each block is parsed on its own so a single malformed block never suppresses the
/// others. Within a block, a JobPosting can appear as the root object, inside an <c>@graph</c> array, or as
/// an item of a top-level array — all three shapes are flattened to the same node list.
/// </summary>
internal static class JsonLdExtractor
{
    private const string ScriptOpen = "<script";
    private const string ScriptClose = "</script>";
    private const string LdJsonMarker = "application/ld+json";
    private const string JobPostingType = "JobPosting";

    /// <summary>
    /// Returns every JobPosting node found on the page, in document order, cloned so the owning
    /// <see cref="JsonDocument"/> can be disposed. Malformed blocks are skipped silently — the caller
    /// counts what it kept, not what the page got wrong.
    /// </summary>
    public static IReadOnlyList<JsonElement> JobPostings(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var nodes = new List<JsonElement>();
        foreach (var block in LdJsonBlocks(html))
        {
            CollectFromBlock(block, nodes);
        }

        return nodes;
    }

    private static void CollectFromBlock(string block, List<JsonElement> nodes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(block);
        }
        catch (JsonException)
        {
            // One malformed block does not prevent parsing the others (T07 "Done when").
            return;
        }

        using (document)
        {
            CollectNodes(document.RootElement, nodes);
        }
    }

    private static void CollectNodes(JsonElement element, List<JsonElement> nodes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectNodes(item, nodes);
                }

                break;

            case JsonValueKind.Object:
                if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    CollectNodes(graph, nodes);
                }

                if (IsJobPosting(element))
                {
                    nodes.Add(element.Clone());
                }

                break;

            default:
                break;
        }
    }

    private static bool IsJobPosting(JsonElement node)
    {
        if (!node.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => string.Equals(type.GetString(), JobPostingType, StringComparison.Ordinal),
            JsonValueKind.Array => type.EnumerateArray().Any(static t =>
                t.ValueKind == JsonValueKind.String
                && string.Equals(t.GetString(), JobPostingType, StringComparison.Ordinal)),
            _ => false,
        };
    }

    private static IEnumerable<string> LdJsonBlocks(string html)
    {
        var cursor = 0;
        while (cursor < html.Length)
        {
            var open = html.IndexOf(ScriptOpen, cursor, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                yield break;
            }

            var tagEnd = html.IndexOf('>', open);
            if (tagEnd < 0)
            {
                yield break;
            }

            var openingTag = html.AsSpan(open, tagEnd - open);
            if (openingTag.Contains(LdJsonMarker, StringComparison.OrdinalIgnoreCase))
            {
                var contentStart = tagEnd + 1;
                var close = html.IndexOf(ScriptClose, contentStart, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    yield break;
                }

                yield return html[contentStart..close];
                cursor = close + ScriptClose.Length;
            }
            else
            {
                cursor = tagEnd + 1;
            }
        }
    }
}
