using JobHunter.Claude.Prompts;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Prompts;

/// <summary>
/// T05: the Domain-port implementation that turns the market-note batch's single result item's raw tool-use
/// JSON into the narrative text — or a recorded failure the synthesiser turns into a template fallback
/// (F5 SAD §6.1, ADR-F5-0001). Every path is asserted against literal JSON: a well-formed note parses and is
/// trimmed; a null/blank payload, malformed JSON, a non-object root, or a missing/non-string/blank
/// <c>narrative</c> is a failure — never an exception. A market note is not a scored item, so there is no
/// reasons array to enforce here.
/// </summary>
public sealed class NarrativeResultParserTests
{
    private readonly NarrativeResultParser _parser = new();

    [Fact]
    public void A_well_formed_note_parses_and_is_trimmed()
    {
        var outcome = _parser.Parse("""{"narrative":"  A steady day: 42 new roles, six a strong match.  "}""");

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Narrative.ShouldBe("A steady day: 42 new roles, six a strong match.");
        outcome.FailureReason.ShouldBeNull();
    }

    [Fact]
    public void An_extra_field_is_ignored_the_narrative_is_still_read()
    {
        var outcome = _parser.Parse("""{"narrative":"Quiet market today.","note":"ignored"}""");

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Narrative.ShouldBe("Quiet market today.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_null_or_blank_payload_is_a_recorded_failure(string? rawJson)
    {
        var outcome = _parser.Parse(rawJson);

        outcome.IsSuccess.ShouldBeFalse();
        outcome.Narrative.ShouldBeNull();
        outcome.FailureReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Malformed_json_is_a_recorded_failure_not_an_exception()
    {
        var outcome = _parser.Parse("{not json");

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason!.ShouldContain("malformed JSON");
    }

    [Fact]
    public void A_non_object_root_is_a_recorded_failure()
    {
        var outcome = _parser.Parse("""["narrative"]""");

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason!.ShouldContain("not a JSON object");
    }

    [Fact]
    public void A_missing_narrative_field_is_a_recorded_failure()
    {
        var outcome = _parser.Parse("""{"summary":"wrong field name"}""");

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason!.ShouldContain("narrative");
    }

    [Fact]
    public void A_non_string_narrative_is_a_recorded_failure()
    {
        var outcome = _parser.Parse("""{"narrative":42}""");

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason!.ShouldContain("narrative");
    }

    [Fact]
    public void A_blank_narrative_is_a_recorded_failure()
    {
        var outcome = _parser.Parse("""{"narrative":"   "}""");

        outcome.IsSuccess.ShouldBeFalse();
        outcome.FailureReason!.ShouldContain("blank");
    }
}
