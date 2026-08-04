using JobHunter.Claude.Prompts;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T04: the tolerant match parser turns one batch item's raw tool-use JSON into a <see cref="MatchOutput"/>
/// or a recorded failure, never throwing for a single item (QG-3, match-schema §Parsing rules). These
/// assert the rules that matter at 03:00: an out-of-range score clamps, an unrecognised interview band
/// degrades to <c>Low</c>, an empty reasons list is a failure (AC-02), and a salary expectation is
/// validated, swapped or dropped.
/// </summary>
public sealed class TolerantMatchParserTests
{
    [Fact]
    public void A_well_formed_result_parses_with_all_fields()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":82,"interviewProbability":"Good","missingSkills":["Rust"],
             "salaryExpectation":{"min":90000,"max":120000,"currency":"EUR","period":"Year"},
             "reasons":["Seven years of Go against a role that names Go as core"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        var o = result.Output!;
        o.MatchScore.ShouldBe(82);
        o.InterviewProbability.ShouldBe(InterviewProbability.Good);
        o.MissingSkills.ShouldBe(["Rust"]);
        o.SalaryExpectation!.Min.ShouldBe(90000m);
        o.SalaryExpectation!.Max.ShouldBe(120000m);
        o.SalaryExpectation!.Currency.ShouldBe("EUR");
        o.SalaryExpectation!.Period.ShouldBe(SalaryPeriod.Year);
        o.Reasons.Count.ShouldBe(1);
    }

    [Fact]
    public void An_empty_missing_skills_list_is_meaningful_and_kept()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":90,"interviewProbability":"Strong","missingSkills":[],"salaryExpectation":null,
             "reasons":["Direct match on every named requirement"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.MissingSkills.ShouldBeEmpty();
        result.Output!.SalaryExpectation.ShouldBeNull();
    }

    [Theory]
    [InlineData(120, 100)]
    [InlineData(-5, 0)]
    public void An_out_of_range_score_is_clamped_and_noted(int raw, int expected)
    {
        var result = TolerantMatchParser.Parse(
            $$"""
            {"matchScore":{{raw}},"interviewProbability":"Low","missingSkills":[],"reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.MatchScore.ShouldBe(expected);
        result.Anomalies.ShouldContain(a => a.Contains("clamped"));
    }

    [Fact]
    public void An_unrecognised_interview_probability_degrades_to_low_and_is_noted()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":50,"interviewProbability":"Certain","missingSkills":[],"reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.InterviewProbability.ShouldBe(InterviewProbability.Low);
        result.Anomalies.ShouldContain(a => a.Contains("interviewProbability"));
    }

    [Fact]
    public void An_unknown_band_on_the_wire_is_not_accepted_as_a_value()
    {
        // "Unknown" is a domain sentinel, not a wire band — it must degrade to Low, not pass through.
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":50,"interviewProbability":"Unknown","missingSkills":[],"reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.InterviewProbability.ShouldBe(InterviewProbability.Low);
        result.Anomalies.ShouldContain(a => a.Contains("interviewProbability"));
    }

    [Fact]
    public void An_inverted_salary_range_is_swapped_and_noted()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[],
             "salaryExpectation":{"min":120000,"max":90000,"currency":"EUR","period":"Year"},
             "reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.SalaryExpectation!.Min.ShouldBe(90000m);
        result.Output!.SalaryExpectation!.Max.ShouldBe(120000m);
        result.Anomalies.ShouldContain(a => a.Contains("inverted"));
    }

    [Fact]
    public void A_salary_with_an_absent_period_defaults_to_year_and_is_noted()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[],
             "salaryExpectation":{"min":90000,"max":120000,"currency":"eur"},
             "reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.SalaryExpectation!.Period.ShouldBe(SalaryPeriod.Year);
        result.Output!.SalaryExpectation!.Currency.ShouldBe("EUR");
        result.Anomalies.ShouldContain(a => a.Contains("period"));
    }

    [Fact]
    public void An_empty_reasons_list_is_a_failure_invariant_4()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[],"reasons":[]}
            """);

        result.IsSuccess.ShouldBeFalse();
        result.Output.ShouldBeNull();
        result.FailureReason.ShouldNotBeNull().ShouldContain("reasons");
    }

    [Fact]
    public void Reasons_are_capped_at_five()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[],
             "reasons":["a","b","c","d","e","f","g"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.Reasons.Count.ShouldBe(5);
    }

    [Fact]
    public void Missing_skills_are_capped_at_ten()
    {
        var skills = string.Join(",", Enumerable.Range(0, 20).Select(i => $"\"skill{i}\""));
        var result = TolerantMatchParser.Parse(
            $$"""
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[{{skills}}],"reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeTrue();
        result.Output!.MissingSkills.Count.ShouldBe(10);
    }

    [Fact]
    public void A_negative_salary_amount_is_a_failure()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[],
             "salaryExpectation":{"min":-1,"max":90000,"currency":"EUR","period":"Year"},
             "reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("negative");
    }

    [Fact]
    public void A_salary_missing_a_currency_is_a_failure()
    {
        var result = TolerantMatchParser.Parse(
            """
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[],
             "salaryExpectation":{"min":90000,"max":120000,"period":"Year"},
             "reasons":["r"]}
            """);

        result.IsSuccess.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("currency");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"matchScore\":\"high\"}")]
    [InlineData("{\"matchScore\":80,\"interviewProbability\":\"Good\",\"missingSkills\":[]}")]
    public void A_malformed_or_incomplete_payload_is_a_recorded_failure_not_a_throw(string? rawJson)
    {
        var result = TolerantMatchParser.Parse(rawJson);

        result.IsSuccess.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNullOrWhiteSpace();
    }
}
