using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Research;

/// <summary>
/// A <see cref="ResearchSource"/> is one fetched document, stored before synthesis (data-model
/// §research_sources, SAD §4 S2). It is the citation authority: its URL is what a claim must later match,
/// and its fetch time (<see cref="ResearchSource.ObservedAt"/>) is the date a claim inherits (AC-03).
/// </summary>
public sealed class ResearchSourceTests
{
    private static readonly Guid SourceId = Guid.Parse("00000000-0000-0000-0000-0000000000F1");
    private static readonly DateTimeOffset ObservedAt = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

    private static ResearchSource NewSource(
        string url = "https://example.com/blog/scaling",
        string title = "How we scaled",
        int textLength = 4200) =>
        new(SourceId, ResearchCategory.EngineeringBlog, url, title, textLength, ObservedAt);

    [Fact]
    public void A_valid_source_exposes_its_fields()
    {
        var source = NewSource();

        source.Id.ShouldBe(SourceId);
        source.Category.ShouldBe(ResearchCategory.EngineeringBlog);
        source.Url.ShouldBe("https://example.com/blog/scaling");
        source.Title.ShouldBe("How we scaled");
        source.TextLength.ShouldBe(4200);
        source.ObservedAt.ShouldBe(ObservedAt);
    }

    [Fact]
    public void A_source_without_an_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            new ResearchSource(Guid.Empty, ResearchCategory.News, "https://example.com", "t", 1, ObservedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_source_without_a_url_is_rejected(string url)
    {
        Should.Throw<ArgumentException>(() => NewSource(url: url));
    }

    [Fact]
    public void A_negative_text_length_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewSource(textLength: -1));
    }

    [Fact]
    public void A_blank_title_is_allowed_because_some_feeds_omit_it()
    {
        // A missing title degrades presentation, not citation; the URL is what verification turns on.
        var source = NewSource(title: "");

        source.Title.ShouldBe("");
    }
}
