using System.Text.Json;
using JobHunter.Claude.Enrichment;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Enrichment;

/// <summary>
/// T08: the schema is generated from the domain enums, so it cannot drift from
/// <see cref="EnrichmentOutput"/> (enrichment-schema §schema, ADR-0006). These assert the invariant-4
/// encoding (<c>reasons.minItems = 1</c>) and that the generated enum lists omit the <c>Unknown</c>
/// sentinel — the model is constrained to real values while the parser still degrades to <c>Unknown</c>.
/// </summary>
public sealed class EnrichmentSchemaTests
{
    private static JsonElement Root()
    {
        var schema = EnrichmentSchema.Build();
        var doc = JsonDocument.Parse(schema.SchemaJson);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Schema_binds_to_the_tool_name()
    {
        EnrichmentSchema.Build().ToolName.ShouldBe("record_enrichment");
    }

    [Fact]
    public void Reasons_encodes_invariant_four_with_min_items_one()
    {
        var reasons = Root().GetProperty("properties").GetProperty("reasons");
        reasons.GetProperty("minItems").GetInt32().ShouldBe(1);
        reasons.GetProperty("maxItems").GetInt32().ShouldBe(6);
    }

    [Fact]
    public void Enum_lists_omit_the_unknown_sentinel()
    {
        var props = Root().GetProperty("properties");

        foreach (var name in new[] { "timezoneBand", "aiUsage", "companyStage" })
        {
            var values = props.GetProperty(name).GetProperty("enum").EnumerateArray()
                .Select(v => v.GetString())
                .ToList();
            values.ShouldNotContain("Unknown");
        }
    }

    [Fact]
    public void Company_stage_lists_the_real_values()
    {
        var values = Root().GetProperty("properties").GetProperty("companyStage").GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()).ToList();

        values.ShouldContain("Seed");
        values.ShouldContain("Public");
        values.ShouldContain("Bootstrapped");
    }

    [Fact]
    public void Salary_is_object_or_null_with_a_currency_pattern()
    {
        var salary = Root().GetProperty("properties").GetProperty("salary");
        var types = salary.GetProperty("type").EnumerateArray().Select(v => v.GetString()).ToList();
        types.ShouldContain("object");
        types.ShouldContain("null");

        salary.GetProperty("properties").GetProperty("currency").GetProperty("pattern").GetString()
            .ShouldBe("^[A-Z]{3}$");
    }

    [Fact]
    public void Technologies_are_capped_at_twenty_five()
    {
        Root().GetProperty("properties").GetProperty("technologies").GetProperty("maxItems").GetInt32()
            .ShouldBe(25);
    }
}
