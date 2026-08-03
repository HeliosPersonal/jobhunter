using System.Text.Json;

namespace JobHunter.Scrapers.Parsing;

/// <summary>
/// Walks a JSON board payload and hands back the byte range of each posting element, one at a time, so a
/// 400-posting board is processed without ever materialising 400 posting objects at once (SAD §5). It is
/// truncation-tolerant by design: if the body is cut off mid-element — the <c>malformed-truncated</c>
/// fixture — the ranges gathered before the break are returned and the tail is dropped, so the postings
/// that did arrive intact survive (QG-1).
/// </summary>
internal static class JsonArrayStreamer
{
    /// <summary>
    /// Returns the byte ranges of each element of the target array. <paramref name="arrayProperty"/> names
    /// the property holding the array (e.g. <c>jobs</c>); <see langword="null"/> means the root itself is
    /// the array (Lever). Ranges index into <paramref name="utf8"/>.
    /// </summary>
    public static IReadOnlyList<Range> ElementRanges(ReadOnlySpan<byte> utf8, string? arrayProperty)
    {
        var ranges = new List<Range>();
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { AllowTrailingCommas = true });

        try
        {
            if (!PositionAtArrayStart(ref reader, arrayProperty))
            {
                return ranges;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                var start = (int)reader.TokenStartIndex;
                if (!reader.TrySkip())
                {
                    // The element is present but incomplete (truncated tail): stop, keep what we have.
                    break;
                }

                var end = (int)reader.BytesConsumed;
                ranges.Add(start..end);
            }
        }
        catch (JsonException)
        {
            // A structurally broken tail is not fatal: the elements already collected are valid and are
            // returned. Semantic problems in an individual element are handled by the adapter per posting.
        }

        return ranges;
    }

    private static bool PositionAtArrayStart(ref Utf8JsonReader reader, string? arrayProperty)
    {
        if (arrayProperty is null)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    return true;
                }
            }

            return false;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName
                && reader.CurrentDepth == 1
                && reader.ValueTextEquals(arrayProperty)
                && reader.Read()
                && reader.TokenType == JsonTokenType.StartArray)
            {
                return true;
            }
        }

        return false;
    }
}
