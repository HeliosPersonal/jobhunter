using System.Text.Json;
using JobHunter.Claude.Prompts;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T06: the synthesis schema is generated from the domain <see cref="ResearchCategory"/> enum, so it cannot
/// drift from the output contract (research-schema §Output record, ADR-0006). The schema can require a
/// <c>sourceUrl</c> to be <em>present</em> — it cannot require it to be <em>true</em>, which is what the
/// verifier (T07) is for — so these assert only the shape: a bounded summary and a bounded array of claims,
/// each with its required category, claim, sourceUrl and isWarning fields, the category constrained to the
/// closed set of eight.
/// </summary>
public sealed class ResearchSynthesisSchemaTests
{
    private static JsonElement Root()
    {
        var schema = ResearchSynthesisSchema.Build();
        var doc = JsonDocument.Parse(schema.SchemaJson);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Schema_binds_to_the_tool_name()
    {
        ResearchSynthesisSchema.Build().ToolName.ShouldBe("record_research");
    }

    [Fact]
    public void Summary_and_claims_are_required()
    {
        var required = Root().GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToList();
        required.ShouldContain("summary");
        required.ShouldContain("claims");
    }

    [Fact]
    public void Summary_is_a_bounded_string()
    {
        var summary = Root().GetProperty("properties").GetProperty("summary");
        summary.GetProperty("type").GetString().ShouldBe("string");
        summary.GetProperty("maxLength").GetInt32().ShouldBe(500);
    }

    [Fact]
    public void Claims_is_a_bounded_array_of_objects()
    {
        var claims = Root().GetProperty("properties").GetProperty("claims");
        claims.GetProperty("type").GetString().ShouldBe("array");
        claims.GetProperty("maxItems").GetInt32().ShouldBe(20);
        claims.GetProperty("items").GetProperty("type").GetString().ShouldBe("object");
    }

    [Fact]
    public void Each_claim_requires_category_claim_source_url_and_is_warning()
    {
        var required = Root().GetProperty("properties").GetProperty("claims")
            .GetProperty("items").GetProperty("required")
            .EnumerateArray().Select(v => v.GetString()).ToList();

        required.ShouldBe(["category", "claim", "sourceUrl", "isWarning"], ignoreOrder: true);
    }

    [Fact]
    public void The_claim_category_is_the_closed_set_of_eight()
    {
        var values = Root().GetProperty("properties").GetProperty("claims")
            .GetProperty("items").GetProperty("properties").GetProperty("category")
            .GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToList();

        values.Count.ShouldBe(8);
        foreach (var category in Enum.GetNames<ResearchCategory>())
        {
            values.ShouldContain(category);
        }
    }

    [Fact]
    public void The_claim_text_is_bounded_and_the_source_url_is_a_uri()
    {
        var props = Root().GetProperty("properties").GetProperty("claims")
            .GetProperty("items").GetProperty("properties");

        props.GetProperty("claim").GetProperty("maxLength").GetInt32().ShouldBe(300);
        props.GetProperty("sourceUrl").GetProperty("format").GetString().ShouldBe("uri");
        props.GetProperty("isWarning").GetProperty("type").GetString().ShouldBe("boolean");
    }

    [Fact]
    public void The_optional_stage_is_the_closed_company_stage_set()
    {
        // Firmographic feedback (AC-10): the model may classify a funding/maturity stage from the fetched
        // text. It is optional — absence means "no evidence", never a guess — so it is not in `required`.
        var required = Root().GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToList();
        required.ShouldNotContain("stage");

        var stage = Root().GetProperty("properties").GetProperty("stage");
        var values = stage.GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToList();
        foreach (var name in Enum.GetNames<CompanyStage>())
        {
            values.ShouldContain(name);
        }
    }

    [Fact]
    public void The_optional_employee_band_is_a_bounded_string()
    {
        var required = Root().GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToList();
        required.ShouldNotContain("employeeBand");

        var band = Root().GetProperty("properties").GetProperty("employeeBand");
        band.GetProperty("type").GetString().ShouldBe("string");
        band.GetProperty("maxLength").GetInt32().ShouldBe(60);
    }
}
