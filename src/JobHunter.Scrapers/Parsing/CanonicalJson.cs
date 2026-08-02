using System.Buffers;
using System.Text;
using System.Text.Json;

namespace JobHunter.Scrapers.Parsing;

/// <summary>
/// Produces the deterministic string a posting is hashed over (SAD §8 "content hash"). Two goals: the
/// same content always yields the same bytes regardless of provider key ordering, and volatile fields
/// (timestamps, tracking ids) are removed so a cosmetic re-fetch is not a change (AC-02). Object keys are
/// emitted in ordinal order; named top-level fields can be dropped or replaced (e.g. Greenhouse's
/// HTML-escaped <c>content</c> is replaced by its plain text so markup-only edits do not churn the hash).
/// </summary>
internal static class CanonicalJson
{
    /// <summary>
    /// Canonicalises <paramref name="element"/>: <paramref name="volatileKeys"/> are dropped at the top
    /// level, and any key in <paramref name="transforms"/> has its value replaced by the transform's
    /// result (as a JSON string). All object keys, at every depth, are written in ordinal order.
    /// </summary>
    public static string Canonicalise(
        JsonElement element,
        IReadOnlySet<string> volatileKeys,
        IReadOnlyDictionary<string, Func<JsonElement, string>> transforms)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteTopLevel(writer, element, volatileKeys, transforms);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteTopLevel(
        Utf8JsonWriter writer,
        JsonElement element,
        IReadOnlySet<string> volatileKeys,
        IReadOnlyDictionary<string, Func<JsonElement, string>> transforms)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            WriteSorted(writer, element);
            return;
        }

        writer.WriteStartObject();
        foreach (var property in Ordered(element))
        {
            if (volatileKeys.Contains(property.Name))
            {
                continue;
            }

            if (transforms.TryGetValue(property.Name, out var transform))
            {
                writer.WriteString(property.Name, transform(property.Value));
                continue;
            }

            writer.WritePropertyName(property.Name);
            WriteSorted(writer, property.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteSorted(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in Ordered(element))
                {
                    writer.WritePropertyName(property.Name);
                    WriteSorted(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteSorted(writer, item);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static IEnumerable<JsonProperty> Ordered(JsonElement element) =>
        element.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal);
}
