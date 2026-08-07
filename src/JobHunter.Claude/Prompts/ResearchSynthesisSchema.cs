using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Research;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The tool-use JSON Schema the research synthesiser is bound to (research-schema §Output record, ADR-0006).
/// The claim category is <em>generated</em> from <see cref="ResearchCategory"/>, so a new category is
/// reflected in the schema automatically and it cannot drift from <see cref="ClaimDto"/>. Every claim
/// requires a <c>sourceUrl</c>, which encodes invariant 5 at the schema level — but the schema can only
/// require the URL to be <em>present</em>; requiring it to be <em>true</em> is the verifier's job (T07),
/// because a model can always cite a plausible URL it invented. Generated through <see cref="Utf8JsonWriter"/>
/// so it stays consistent with the other schemas in this layer (mirrors <c>EnrichmentSchema</c>).
/// </summary>
public static class ResearchSynthesisSchema
{
    /// <summary>The tool name the schema binds to.</summary>
    public const string ToolName = "record_research";

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
            w.WriteStringValue("summary");
            w.WriteStringValue("claims");
            w.WriteEndArray();

            w.WriteStartObject("properties");
            WriteSummary(w);
            WriteClaims(w);
            w.WriteEndObject(); // properties

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteSummary(Utf8JsonWriter w)
    {
        w.WriteStartObject("summary");
        w.WriteString("type", "string");
        // Two or three sentences, itself constrained to cited claims — a ceiling keeps a runaway generation
        // from bloating the header. The tolerant parser still trims whatever arrives.
        w.WriteNumber("maxLength", 500);
        w.WriteEndObject();
    }

    private static void WriteClaims(Utf8JsonWriter w)
    {
        w.WriteStartObject("claims");
        w.WriteString("type", "array");
        w.WriteNumber("maxItems", 20);

        w.WriteStartObject("items");
        w.WriteString("type", "object");

        w.WriteStartArray("required");
        foreach (var name in new[] { "category", "claim", "sourceUrl", "isWarning" })
        {
            w.WriteStringValue(name);
        }

        w.WriteEndArray();

        w.WriteStartObject("properties");

        w.WriteStartObject("category");
        w.WriteStartArray("enum");
        foreach (var value in Enum.GetNames<ResearchCategory>())
        {
            w.WriteStringValue(value);
        }

        w.WriteEndArray();
        w.WriteEndObject(); // category

        w.WriteStartObject("claim");
        w.WriteString("type", "string");
        w.WriteNumber("maxLength", 300);
        w.WriteEndObject();

        w.WriteStartObject("sourceUrl");
        w.WriteString("type", "string");
        w.WriteString("format", "uri");
        w.WriteEndObject();

        w.WriteStartObject("isWarning");
        w.WriteString("type", "boolean");
        w.WriteEndObject();

        w.WriteEndObject(); // properties
        w.WriteEndObject(); // items

        w.WriteEndObject(); // claims
    }
}
