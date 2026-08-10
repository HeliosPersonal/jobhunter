using JobHunter.Claude.Prompts;
using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// The tolerant-match parser's residual defensive arms, complementing <see cref="TolerantMatchParserTests"/>:
/// an <c>interviewProbability</c> that is absent versus present-but-non-string (both degrade to Low, not throw);
/// a string array (missing skills / reasons) carrying a non-string element and a blank element (each silently
/// skipped, QG-3); a <c>salaryExpectation</c> that is neither null nor an object; and a salary object whose
/// <c>min</c>/<c>max</c> is absent or non-number (the <c>TryReadDecimal</c> reject arms). None throws for a single
/// item — every malformed shape is a recorded value.
/// </summary>
public sealed class TolerantMatchParserBranchTests
{
    [Fact]
    public void An_absent_interview_probability_degrades_to_low_and_is_noted()
    {
        // The `!TryGetProperty` arm of the interviewProbability guard: the field is simply not present.
        var result = TolerantMatchParser.Parse(
            """{"matchScore":50,"missingSkills":[],"reasons":["r"]}""");

        result.IsSuccess.ShouldBeTrue();
        result.Output!.InterviewProbability.ShouldBe(InterviewProbability.Low);
        result.Anomalies.ShouldContain(a => a.Contains("absent or non-string"));
    }

    [Fact]
    public void A_non_string_interview_probability_degrades_to_low_and_is_noted()
    {
        // The `ValueKind != String` arm: the field is present but the model emitted a number.
        var result = TolerantMatchParser.Parse(
            """{"matchScore":50,"interviewProbability":42,"missingSkills":[],"reasons":["r"]}""");

        result.IsSuccess.ShouldBeTrue();
        result.Output!.InterviewProbability.ShouldBe(InterviewProbability.Low);
        result.Anomalies.ShouldContain(a => a.Contains("absent or non-string"));
    }

    [Fact]
    public void A_non_string_element_in_a_string_array_is_skipped()
    {
        // ReadStringArray's non-string continue arm: the number and null are dropped, the strings survive.
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Good","missingSkills":["Rust",7,null,"Go"],
             "reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.MissingSkills.ShouldBe(["Rust", "Go"]);
    }

    [Fact]
    public void A_blank_string_element_in_a_string_array_is_skipped()
    {
        // ReadStringArray's blank continue arm: the whitespace-only entry is dropped, the real one survives.
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Good","missingSkills":["   ","Kubernetes"],
             "reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.MissingSkills.ShouldBe(["Kubernetes"]);
    }

    [Fact]
    public void A_salary_expectation_that_is_neither_object_nor_null_is_a_failure()
    {
        // ReadSalaryExpectation's not-an-object arm: a bare number is present but unusable.
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Good","missingSkills":[],
             "salaryExpectation":123,"reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("neither an object nor null");
    }

    [Fact]
    public void A_salary_expectation_missing_min_is_a_failure()
    {
        // TryReadDecimal's absent arm on 'min': the property is not present at all.
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Good","missingSkills":[],
             "salaryExpectation":{"max":120000,"currency":"EUR","period":"Year"},"reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("min");
    }

    [Fact]
    public void A_salary_expectation_with_a_non_number_max_is_a_failure()
    {
        // TryReadDecimal's non-number arm on 'max': present, but a string rather than a number.
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Good","missingSkills":[],
             "salaryExpectation":{"min":90000,"max":"lots","currency":"EUR","period":"Year"},"reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("min");
    }
}
