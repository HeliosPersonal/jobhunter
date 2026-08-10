using JobHunter.Scrapers.Parsing;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Parsing;

/// <summary>
/// The awkward corners of the single-pass HTML micro-parser (T04): an unterminated tag or comment at the
/// end of the document, a raw-text element that is self-closing, unterminated, or only looks like
/// <c>&lt;script&gt;</c>, a non-positive cap, and a cap that must fall inside a single unbroken word. Each
/// case drives one otherwise-unexercised arm; none reaches the model as JavaScript, CSS, or a mid-word cut.
/// </summary>
public sealed class ResearchContentExtractorBranchTests
{
    [Fact]
    public void UnterminatedTag_atEndOfDocument_dropsTheRemainder()
    {
        // "<span" has no closing '>' — the rest of the document is treated as gone, the prior text survives.
        ResearchContentExtractor.ToPlainText("<p>Kept</p><span")
            .ShouldBe("Kept");
    }

    [Fact]
    public void UnterminatedComment_atEndOfDocument_swallowsTheRest()
    {
        ResearchContentExtractor.ToPlainText("<p>Kept</p><!-- dangling note with no terminator")
            .ShouldBe("Kept");
    }

    [Fact]
    public void TagThatMerelyStartsLikeScript_isTreatedAsInline_notRawElement()
    {
        // "<scripting>" is not "<script" + delimiter, so its body is real text, not a discarded script body.
        ResearchContentExtractor.ToPlainText("<p>a</p><scripting>b</scripting>c")
            .ShouldBe("a\n\nb c");
    }

    [Fact]
    public void SelfClosingScript_endsAtItsOwnBracket_bodyAfterItSurvives()
    {
        ResearchContentExtractor.ToPlainText("<p>A</p><script/><p>B</p>")
            .ShouldBe("A\n\nB");
    }

    [Fact]
    public void UnterminatedScriptOpenTag_swallowsTheRest()
    {
        // "<script var x" never closes its own '>' — everything after it is gone, "A" is kept.
        ResearchContentExtractor.ToPlainText("<p>A</p><script var x")
            .ShouldBe("A");
    }

    [Fact]
    public void ScriptWithoutAClosingTag_discardsToEndOfDocument()
    {
        // The "<script>" opens and never closes — its body (and everything after) is discarded.
        ResearchContentExtractor.ToPlainText("<p>A</p><script>var x = 1; more and more")
            .ShouldBe("A");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void NonPositiveCap_isEmpty(int maxChars)
    {
        ResearchContentExtractor.ToPlainText("<p>Some real text here</p>", maxChars)
            .ShouldBeEmpty();
    }

    [Fact]
    public void SingleUnbrokenWordOverTheCap_cutsAtTheCap_withNoSpaceToBackOffTo()
    {
        var word = new string('x', 100);

        var result = ResearchContentExtractor.ToPlainText($"<p>{word}</p>", maxChars: 40);

        // No space in the slice, so the cut is the raw cap length — the lastSpace <= 0 arm.
        result.Length.ShouldBe(40);
        result.ShouldBe(new string('x', 40));
    }
}
