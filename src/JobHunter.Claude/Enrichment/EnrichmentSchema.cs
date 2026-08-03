using System.Text.Json;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Claude.Enrichment;

/// <summary>
/// The tool-use JSON Schema the model is bound to (enrichment-schema §schema, ADR-0006). It is
/// <em>generated</em> from the domain enums rather than hand-written, so a new enum member is reflected in
/// the schema automatically and the schema cannot drift from <see cref="EnrichmentOutput"/>. The
/// generation deliberately omits each enum's <c>Unknown</c> sentinel: the model is constrained to the real
/// values, while the tolerant parser still lands an unexpected value on <c>Unknown</c> (parsing step 8).
/// <c>reasons</c> carries <c>minItems: 1</c>, encoding invariant 4 at the schema level so the provider
/// constrains generation rather than us rejecting afterwards.
/// </summary>
public static class EnrichmentSchema
{
    /// <summary>The tool name the schema binds to.</summary>
    public const string ToolName = "record_enrichment";

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
            foreach (var name in new[]
                     {
                         "isRemote", "isContractorFriendly", "timezoneBand", "aiUsage", "companyStage",
                         "roleFamily", "technologies", "reasons",
                     })
            {
                w.WriteStringValue(name);
            }

            w.WriteEndArray();

            w.WriteStartObject("properties");

            WriteSalary(w);
            WriteBoolean(w, "isRemote");
            WriteBoolean(w, "isContractorFriendly");
            WriteEnum(w, "timezoneBand", EnumValuesExceptUnknown<TimezoneBand>());
            WriteEnum(w, "aiUsage", EnumValuesExceptUnknown<AiUsageLevel>());

            // aiSignals resolves the scalar (TUNE-04). It is deliberately optional — the model supplies the
            // sub-signals when the work supports them, and an absent object degrades to all-false in the parser.
            WriteAiSignals(w);
            WriteEnum(w, "companyStage", EnumValuesExceptUnknown<CompanyStage>());

            // roleFamily is a closed enum whose 'Other' member is a legitimate classification, not a
            // parse sentinel like the Unknown members above — so the model is bound to every value.
            WriteEnum(w, "roleFamily", Enum.GetNames<RoleFamily>());
            WriteStringArray(w, "technologies", minItems: null, maxItems: 25);
            WriteStringArray(w, "reasons", minItems: 1, maxItems: 6);

            w.WriteEndObject(); // properties
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteSalary(Utf8JsonWriter w)
    {
        w.WriteStartObject("salary");
        w.WriteStartArray("type");
        w.WriteStringValue("object");
        w.WriteStringValue("null");
        w.WriteEndArray();

        w.WriteStartArray("required");
        foreach (var name in new[] { "min", "max", "currency", "period", "confidence" })
        {
            w.WriteStringValue(name);
        }

        w.WriteEndArray();

        w.WriteStartObject("properties");
        WriteNumber(w, "min", minimum: 0, maximum: null);
        WriteNumber(w, "max", minimum: 0, maximum: null);

        w.WriteStartObject("currency");
        w.WriteString("type", "string");
        w.WriteString("pattern", "^[A-Z]{3}$");
        w.WriteEndObject();

        WriteEnum(w, "period", Enum.GetNames<SalaryPeriod>());
        WriteNumber(w, "confidence", minimum: 0, maximum: 1);
        w.WriteEndObject(); // properties

        w.WriteEndObject(); // salary
    }

    private static void WriteAiSignals(Utf8JsonWriter w)
    {
        w.WriteStartObject("aiSignals");
        w.WriteString("type", "object");
        w.WriteStartObject("properties");
        WriteBoolean(w, "buildsAiProduct");
        WriteBoolean(w, "buildsAiInfra");
        WriteBoolean(w, "usesAiTooling");
        WriteBoolean(w, "isResearch");
        w.WriteEndObject(); // properties
        w.WriteEndObject(); // aiSignals
    }

    private static void WriteBoolean(Utf8JsonWriter w, string name)
    {
        w.WriteStartObject(name);
        w.WriteString("type", "boolean");
        w.WriteEndObject();
    }

    private static void WriteNumber(Utf8JsonWriter w, string name, double? minimum, double? maximum)
    {
        w.WriteStartObject(name);
        w.WriteString("type", "number");
        if (minimum is { } min)
        {
            w.WriteNumber("minimum", min);
        }

        if (maximum is { } max)
        {
            w.WriteNumber("maximum", max);
        }

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
