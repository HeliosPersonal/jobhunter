using JobHunter.Scrapers.Parsing;
using Shouldly;
using Xunit;

namespace JobHunter.Scrapers.Tests.Parsing;

/// <summary>
/// The decode-then-strip convention Greenhouse forces and the other adapters reuse. Double-decoding is
/// idempotent on already-plain text, so a provider that escapes once is unharmed by the second pass.
/// </summary>
public sealed class HtmlTextTests
{
    [Fact]
    public void DoubleEscapedHtml_isDecodedTwiceThenStripped()
    {
        // &lt;p&gt; decodes to <p>, which strips to nothing, leaving the text.
        HtmlText.ToPlainText("&lt;p&gt;Hello &lt;b&gt;world&lt;/b&gt;&lt;/p&gt;")
            .ShouldBe("Hello world");
    }

    [Fact]
    public void Entities_areResolved()
    {
        HtmlText.ToPlainText("Café &amp;amp; Résumé").ShouldBe("Café & Résumé");
    }

    [Fact]
    public void Whitespace_isCollapsed()
    {
        HtmlText.ToPlainText("&lt;p&gt;a&lt;/p&gt;\n\n   &lt;p&gt;b&lt;/p&gt;").ShouldBe("a b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_isEmpty(string? input)
    {
        HtmlText.ToPlainText(input).ShouldBe(string.Empty);
    }

    [Fact]
    public void PlainText_isUnchanged()
    {
        HtmlText.ToPlainText("Just plain text").ShouldBe("Just plain text");
    }
}
