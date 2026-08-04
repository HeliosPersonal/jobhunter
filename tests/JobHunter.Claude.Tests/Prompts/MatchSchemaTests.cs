using System.Text.Json;
using JobHunter.Claude.Prompts;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T04: the match schema is generated from the domain enums and constants, so it cannot drift from
/// <see cref="MatchOutput"/> and the <see cref="JobHunter.Domain.Intelligence.Match"/> aggregate
/// (match-schema §Output record, ADR-0006). These assert the invariant-4 encoding
/// (<c>reasons.minItems = 1</c>), the interview-probability enum omitting the <c>Unknown</c> sentinel, the
/// score bounds and the missing-skills cap.
/// </summary>
public sealed class MatchSchemaTests
{
    private static JsonElement Root()
    {
        var schema = MatchSchema.Build();
        var doc = JsonDocument.Parse(schema.SchemaJson);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Schema_binds_to_the_tool_name()
    {
        MatchSchema.Build().ToolName.ShouldBe("record_match");
    }

    [Fact]
    public void Reasons_encodes_invariant_four_with_min_items_one()
    {
        var reasons = Root().GetProperty("properties").GetProperty("reasons");
        reasons.GetProperty("minItems").GetInt32().ShouldBe(1);
        reasons.GetProperty("maxItems").GetInt32().ShouldBe(5);
    }

    [Fact]
    public void Match_score_is_an_integer_bounded_zero_to_one_hundred()
    {
        var score = Root().GetProperty("properties").GetProperty("matchScore");
        score.GetProperty("type").GetString().ShouldBe("integer");
        score.GetProperty("minimum").GetInt32().ShouldBe(0);
        score.GetProperty("maximum").GetInt32().ShouldBe(100);
    }

    [Fact]
    public void Interview_probability_omits_the_unknown_sentinel()
    {
        var values = Root().GetProperty("properties").GetProperty("interviewProbability")
            .GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToList();

        values.ShouldBe(["Low", "Moderate", "Good", "Strong"]);
        values.ShouldNotContain("Unknown");
    }

    [Fact]
    public void Missing_skills_are_capped_at_ten()
    {
        Root().GetProperty("properties").GetProperty("missingSkills").GetProperty("maxItems").GetInt32()
            .ShouldBe(10);
    }

    [Fact]
    public void Required_fields_are_score_probability_missing_skills_and_reasons()
    {
        var required = Root().GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToList();

        required.ShouldBe(["matchScore", "interviewProbability", "missingSkills", "reasons"]);
        // salaryExpectation is deliberately optional — a null is a legal "cannot tell".
        required.ShouldNotContain("salaryExpectation");
    }

    [Fact]
    public void Salary_expectation_is_object_or_null_with_a_currency_pattern_and_period_enum()
    {
        var salary = Root().GetProperty("properties").GetProperty("salaryExpectation");
        var types = salary.GetProperty("type").EnumerateArray().Select(v => v.GetString()).ToList();
        types.ShouldContain("object");
        types.ShouldContain("null");

        salary.GetProperty("properties").GetProperty("currency").GetProperty("pattern").GetString()
            .ShouldBe("^[A-Z]{3}$");

        var periods = salary.GetProperty("properties").GetProperty("period").GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()).ToList();
        periods.ShouldContain("Year");
        periods.ShouldContain("Hour");
    }
}
