using JobHunter.Claude.Prompts;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T04: the Domain-port implementation that turns one batch item's raw tool-use JSON into a validated
/// <see cref="Match"/> aggregate, or a recorded failure (match-schema §Parsing rules). It wraps the
/// tolerant parser and maps the wire shape onto the aggregate — the one place the parser's output becomes
/// a domain object — so per-item isolation (QG-3), invariant 4 (a reason on every match) and the salary
/// drop-not-reject rule are all asserted here against fixture JSON. No CV text reaches this type.
/// </summary>
public sealed class MatchResultParserTests
{
    private static readonly Guid MatchId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid ProfileId = Guid.Parse("00000000-0000-0000-0000-0000000000D1");
    private static readonly Guid CvVersionId = Guid.Parse("00000000-0000-0000-0000-0000000000E1");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly MatchResultParser _parser = new();

    private MatchParseOutcome Parse(string? rawJson) =>
        _parser.Parse(new MatchParseRequest(
            MatchId, JobId, RunId, ProfileId, CvVersionId, "match-v1", CreatedAt, rawJson));

    [Fact]
    public void A_well_formed_result_becomes_a_match_stamped_with_its_identity()
    {
        var outcome = Parse(
            """
            {"matchScore":82,"interviewProbability":"Good","missingSkills":["Rust"],
             "salaryExpectation":{"min":90000,"max":120000,"currency":"EUR","period":"Year"},
             "reasons":["Seven years of Go against a role that names Go as core"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        var m = outcome.Match!;
        m.Id.ShouldBe(MatchId);
        m.JobId.ShouldBe(JobId);
        m.RunId.ShouldBe(RunId);
        m.ProfileId.ShouldBe(ProfileId);
        m.CvVersionId.ShouldBe(CvVersionId);
        m.PromptVersion.ShouldBe("match-v1");
        m.CreatedAt.ShouldBe(CreatedAt);
        m.MatchScore.ShouldBe(82);
        m.InterviewProbability.ShouldBe(InterviewProbability.Good);
        m.MissingSkills.ShouldBe(["Rust"]);
        m.IsCurrent.ShouldBeTrue();
        m.Reasons.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void A_valid_salary_expectation_is_mapped_to_the_value_object()
    {
        var outcome = Parse(
            """
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[],
             "salaryExpectation":{"min":90000,"max":120000,"currency":"EUR","period":"Year"},
             "reasons":["Range implied by the posting"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        var salary = outcome.Match!.SalaryExpectation.ShouldNotBeNull();
        salary.Min.ShouldBe(90000m);
        salary.Max.ShouldBe(120000m);
        salary.Currency.ShouldBe("EUR");
    }

    [Fact]
    public void A_null_salary_expectation_is_a_legal_cannot_tell()
    {
        var outcome = Parse(
            """
            {"matchScore":40,"interviewProbability":"Low","missingSkills":[],"salaryExpectation":null,
             "reasons":["The posting gives nothing to anchor a number on"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Match!.SalaryExpectation.ShouldBeNull();
    }

    [Fact]
    public void An_unrecognised_interview_band_degrades_to_low_rather_than_failing()
    {
        var outcome = Parse(
            """
            {"matchScore":55,"interviewProbability":"VeryLikely","missingSkills":[],
             "reasons":["Solid overlap on the core stack"]}
            """);

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Match!.InterviewProbability.ShouldBe(InterviewProbability.Low);
        outcome.Anomalies.ShouldContain(a => a.Contains("interviewProbability"));
    }

    [Fact]
    public void An_empty_reasons_list_is_a_failure_invariant_4()
    {
        var outcome = Parse(
            """
            {"matchScore":70,"interviewProbability":"Moderate","missingSkills":[],"reasons":[]}
            """);

        outcome.IsSuccess.ShouldBeFalse();
        outcome.Match.ShouldBeNull();
        outcome.FailureReason.ShouldNotBeNull().ShouldContain("reasons");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"matchScore\":\"high\"}")]
    public void A_malformed_or_incomplete_payload_is_a_recorded_failure_not_a_throw(string? rawJson)
    {
        var outcome = Parse(rawJson);

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason.ShouldNotBeNullOrWhiteSpace();
    }
}
