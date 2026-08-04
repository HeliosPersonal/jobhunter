using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Claude.Prompts;

/// <summary>
/// The tool-use JSON Schema the model is bound to for matching (match-schema §Output record, ADR-0006). It
/// is <em>generated</em> from the domain enums rather than hand-written, so a new enum member is reflected
/// automatically and the schema cannot drift from <see cref="MatchOutput"/>. The generation deliberately
/// omits <see cref="InterviewProbability.Unknown"/>: the model is constrained to the four real bands, while
/// the tolerant parser still lands an unexpected value on <c>Low</c>. <c>reasons</c> carries
/// <c>minItems: 1</c>, encoding invariant 4 at the schema level so the provider constrains generation
/// rather than us rejecting afterwards; <c>missingSkills</c> is capped at 10.
/// </summary>
public static class MatchSchema
{
    /// <summary>The tool name the schema binds to.</summary>
    public const string ToolName = "record_match";

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
            foreach (var name in new[] { "matchScore", "interviewProbability", "missingSkills", "reasons" })
            {
                w.WriteStringValue(name);
            }

            w.WriteEndArray();

            w.WriteStartObject("properties");

            WriteInteger(w, "matchScore", minimum: Match.MinScore, maximum: Match.MaxScore);
            WriteEnum(w, "interviewProbability", EnumValuesExceptUnknown<InterviewProbability>());
            WriteStringArray(w, "missingSkills", minItems: null, maxItems: Match.MaxMissingSkills);
            WriteSalaryExpectation(w);
            WriteStringArray(w, "reasons", minItems: 1, maxItems: 5);

            w.WriteEndObject(); // properties
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteSalaryExpectation(Utf8JsonWriter w)
    {
        w.WriteStartObject("salaryExpectation");
        w.WriteStartArray("type");
        w.WriteStringValue("object");
        w.WriteStringValue("null");
        w.WriteEndArray();

        w.WriteStartArray("required");
        foreach (var name in new[] { "min", "max", "currency", "period" })
        {
            w.WriteStringValue(name);
        }

        w.WriteEndArray();

        w.WriteStartObject("properties");
        WriteNumber(w, "min", minimum: 0);
        WriteNumber(w, "max", minimum: 0);

        w.WriteStartObject("currency");
        w.WriteString("type", "string");
        w.WriteString("pattern", "^[A-Z]{3}$");
        w.WriteEndObject();

        WriteEnum(w, "period", Enum.GetNames<SalaryPeriod>());
        w.WriteEndObject(); // properties

        w.WriteEndObject(); // salaryExpectation
    }

    private static void WriteInteger(Utf8JsonWriter w, string name, int minimum, int maximum)
    {
        w.WriteStartObject(name);
        w.WriteString("type", "integer");
        w.WriteNumber("minimum", minimum);
        w.WriteNumber("maximum", maximum);
        w.WriteEndObject();
    }

    private static void WriteNumber(Utf8JsonWriter w, string name, double minimum)
    {
        w.WriteStartObject(name);
        w.WriteString("type", "number");
        w.WriteNumber("minimum", minimum);
        w.WriteEndObject();
    }

    private static void WriteEnum(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        w.WriteStartObject(name);
        w.WriteStartArray("enum");
        foreach (var value in values)
        {
            w.WriteStringValue(value);
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter w, string name, int? minItems, int? maxItems)
    {
        w.WriteStartObject(name);
        w.WriteString("type", "array");
        w.WriteStartObject("items");
        w.WriteString("type", "string");
        w.WriteEndObject();
        if (minItems is { } min)
        {
            w.WriteNumber("minItems", min);
        }

        if (maxItems is { } max)
        {
            w.WriteNumber("maxItems", max);
        }

        w.WriteEndObject();
    }

    private static string[] EnumValuesExceptUnknown<TEnum>() where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>()
            .Where(n => !string.Equals(n, "Unknown", StringComparison.Ordinal))
            .ToArray();
}
