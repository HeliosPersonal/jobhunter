using JobHunter.Scrapers.Parsing;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Parsing;

/// <summary>
/// The content-extraction contract for company research (T04 "Done when"): scripts and styles are
/// discarded with their content, inline markup is stripped to plain text, paragraph structure survives
/// so a cap can fall on a paragraph boundary, and a page with no extractable text is no document (there
/// is no headless browser). The cap is 20 000 characters.
/// </summary>
public sealed class ResearchContentExtractorTests
{
    [Fact]
    public void Script_contentIsDiscarded_notJustTheTags()
    {
        ResearchContentExtractor.ToPlainText("<p>Hi</p><script>var x = 1; alert('boo');</script>")
            .ShouldBe("Hi");
    }

    [Fact]
    public void Style_contentIsDiscarded_notJustTheTags()
    {
        ResearchContentExtractor.ToPlainText("<style>.a { color: red; }</style><p>Body</p>")
            .ShouldBe("Body");
    }

    [Fact]
    public void HtmlComments_areDiscarded()
    {
        ResearchContentExtractor.ToPlainText("<p>Kept</p><!-- hidden note -->")
            .ShouldBe("Kept");
    }

    [Fact]
    public void Entities_areDecoded_andTagsStripped()
    {
        ResearchContentExtractor.ToPlainText("<h1>Title &amp; Co</h1>")
            .ShouldBe("Title & Co");
    }

    [Fact]
    public void InlineMarkup_staysWithinItsParagraph()
    {
        // <b> is inline, not a block boundary — the text is one paragraph, not three.
        ResearchContentExtractor.ToPlainText("<p>Long text with <b>bold</b> inside</p>")
            .ShouldBe("Long text with bold inside");
    }

    [Fact]
    public void BlockTags_separateParagraphs()
    {
        ResearchContentExtractor.ToPlainText("<p>One</p><p>Two</p>")
            .ShouldBe("One\n\nTwo");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("<script>x = 1;</script>")]
    [InlineData("<div></div><p>   </p>")]
    public void NoExtractableText_isEmpty(string? html)
    {
        ResearchContentExtractor.ToPlainText(html).ShouldBeEmpty();
    }

    [Fact]
    public void Cap_fallsOnAParagraphBoundary_notMidParagraph()
    {
        var paragraph = new string('A', 100);
        var html = $"<p>{paragraph}</p><p>{new string('B', 100)}</p><p>{new string('C', 100)}</p>";

        // Room for the first paragraph and the boundary, but not the second.
        var result = ResearchContentExtractor.ToPlainText(html, maxChars: 150);

        result.ShouldBe(paragraph);
        result.Length.ShouldBeLessThanOrEqualTo(150);
    }

    [Fact]
    public void Cap_onASingleOversizedParagraph_cutsOnAWordBoundary()
    {
        var html = "<p>" + string.Concat(Enumerable.Repeat("word ", 60)).Trim() + "</p>";

        var result = ResearchContentExtractor.ToPlainText(html, maxChars: 50);

        result.Length.ShouldBeLessThanOrEqualTo(50);
        result.ShouldNotContain("wor\0"); // never cuts inside a word
        result.ShouldEndWith("word");
        result.ShouldNotStartWith(" ");
    }

    [Fact]
    public void ShortText_underTheCap_isReturnedWhole()
    {
        ResearchContentExtractor.ToPlainText("<p>Short and sweet</p>", maxChars: 20_000)
            .ShouldBe("Short and sweet");
    }
}
