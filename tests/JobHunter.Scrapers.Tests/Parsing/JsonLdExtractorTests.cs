using JobHunter.Scrapers.Parsing;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Parsing;

/// <summary>
/// Direct unit cover for the JSON-LD block scanner — the char-scan edge cases that the fixture-driven
/// adapter tests do not naturally reach (unterminated tags, no closing script, casing).
/// </summary>
public sealed class JsonLdExtractorTests
{
    [Fact]
    public void NullHtml_throws()
    {
        Should.Throw<ArgumentNullException>(() => JsonLdExtractor.JobPostings(null!));
    }

    [Fact]
    public void EmptyHtml_returnsNothing()
    {
        JsonLdExtractor.JobPostings(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void ScriptOpenTagNeverClosed_isIgnored()
    {
        // "<script" with no closing ">" — the scan gives up rather than misreading.
        JsonLdExtractor.JobPostings("<script type=\"application/ld+json\"").ShouldBeEmpty();
    }

    [Fact]
    public void LdJsonScriptWithoutClosingTag_isIgnored()
    {
        var html = "<script type=\"application/ld+json\">{\"@type\":\"JobPosting\",\"url\":\"u\"}";

        JsonLdExtractor.JobPostings(html).ShouldBeEmpty();
    }

    [Fact]
    public void CasingOfTypeAttribute_isMatchedCaseInsensitively()
    {
        var html =
            "<SCRIPT TYPE=\"Application/LD+JSON\">" +
            "{\"@type\":\"JobPosting\",\"identifier\":\"x1\",\"url\":\"https://e/x\"}" +
            "</SCRIPT>";

        var nodes = JsonLdExtractor.JobPostings(html);

        nodes.Count.ShouldBe(1);
        nodes[0].GetProperty("identifier").GetString().ShouldBe("x1");
    }

    [Fact]
    public void NonLdJsonScript_isSkipped_andScanContinuesPastIt()
    {
        var html =
            "<script>var x = 1;</script>" +
            "<script type=\"application/ld+json\">{\"@type\":\"JobPosting\",\"url\":\"u\"}</script>";

        JsonLdExtractor.JobPostings(html).Count.ShouldBe(1);
    }

    [Fact]
    public void TypeArrayWithoutJobPosting_isNotAMatch()
    {
        var html =
            "<script type=\"application/ld+json\">" +
            "{\"@type\":[\"Organization\",\"Thing\"],\"url\":\"u\"}" +
            "</script>";

        JsonLdExtractor.JobPostings(html).ShouldBeEmpty();
    }

    [Fact]
    public void TypeThatIsNeitherStringNorArray_isNotAMatch()
    {
        var html =
            "<script type=\"application/ld+json\">" +
            "{\"@type\":123,\"url\":\"u\"}" +
            "</script>";

        JsonLdExtractor.JobPostings(html).ShouldBeEmpty();
    }

    [Fact]
    public void NodeWithoutTypeProperty_isNotAMatch()
    {
        var html = "<script type=\"application/ld+json\">{\"url\":\"u\"}</script>";

        JsonLdExtractor.JobPostings(html).ShouldBeEmpty();
    }

    [Fact]
    public void PrimitiveJsonBlock_isTolerated()
    {
        var html = "<script type=\"application/ld+json\">42</script>";

        JsonLdExtractor.JobPostings(html).ShouldBeEmpty();
    }

    [Fact]
    public void GraphThatIsNotAnArray_isIgnoredButOuterStillConsidered()
    {
        var html =
            "<script type=\"application/ld+json\">" +
            "{\"@graph\":{\"@type\":\"JobPosting\"},\"@type\":\"JobPosting\",\"identifier\":\"outer\",\"url\":\"u\"}" +
            "</script>";

        var nodes = JsonLdExtractor.JobPostings(html);

        nodes.Count.ShouldBe(1);
        nodes[0].GetProperty("identifier").GetString().ShouldBe("outer");
    }
}
