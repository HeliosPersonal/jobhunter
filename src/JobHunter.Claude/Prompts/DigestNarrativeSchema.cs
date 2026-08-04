using System.Text.Json;
using JobHunter.Domain.Abstractions;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The tool-use JSON Schema the market-note synthesis is bound to (ADR-0006, F5 T05). It constrains the
/// model to a single object with a non-empty <c>narrative</c> string, so a well-formed generation is prose
/// and nothing else — no scores, no fabricated structure. Generated through <see cref="Utf8JsonWriter"/> so
/// it stays consistent with the other schemas in this layer (mirrors <c>EnrichmentSchema</c>).
/// </summary>
public static class DigestNarrativeSchema
{
    /// <summary>The tool name the schema binds to.</summary>
    public const string ToolName = "record_market_note";

    /// <summary>The narrative field the model must fill and the parser reads.</summary>
    public const string NarrativeField = "narrative";

    /// <summary>The generated schema as a <see cref="JsonSchema"/> ready to hand to <see cref="ILlmBatchClient"/>.</summary>
    public static JsonSchema Build() => new(ToolName, BuildJson());

    private static string BuildJson()
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("type", "object");

            w.WriteStartArray("required");
            w.WriteStringValue(NarrativeField);
            w.WriteEndArray();

            w.WriteStartObject("properties");
            w.WriteStartObject(NarrativeField);
            w.WriteString("type", "string");
            // A market note is two or three sentences; a floor keeps the model from emitting an empty string,
            // and a ceiling keeps a runaway generation from bloating the header. The tolerant parser still
            // accepts anything non-blank and simply trims it, so these bounds shape rather than gate.
            w.WriteNumber("minLength", 1);
            w.WriteNumber("maxLength", 600);
            w.WriteEndObject();
            w.WriteEndObject(); // properties

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
