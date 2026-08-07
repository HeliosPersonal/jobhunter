using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Research;

/// <summary>
/// A <see cref="FetchedDocument"/> is what a fetcher returns and what the orchestrator stores <em>before</em>
/// synthesis (SAD §5). Its <see cref="FetchedDocument.Url"/> is the exact URL retrieved — the citation
/// authority a later claim must match by set membership — so it is required; the text is what the model is
/// given and may legitimately be empty (a page with no extractable text is treated as no document upstream).
/// </summary>
public sealed class FetchedDocumentTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_valid_document_exposes_its_fields()
    {
        var doc = new FetchedDocument("https://example.com/blog/scaling", "How we scaled", "body text", ObservedAt);

        doc.Url.ShouldBe("https://example.com/blog/scaling");
        doc.Title.ShouldBe("How we scaled");
        doc.Text.ShouldBe("body text");
        doc.ObservedAt.ShouldBe(ObservedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_document_without_a_url_is_rejected(string url)
    {
        Should.Throw<ArgumentException>(() => new FetchedDocument(url, "t", "body", ObservedAt));
    }

    [Fact]
    public void A_null_text_is_rejected_because_the_model_is_handed_the_text()
    {
        Should.Throw<ArgumentNullException>(() => new FetchedDocument("https://example.com", "t", null!, ObservedAt));
    }

    [Fact]
    public void A_null_title_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() =>
            new FetchedDocument("https://example.com", null!, "body", ObservedAt));
    }
}
