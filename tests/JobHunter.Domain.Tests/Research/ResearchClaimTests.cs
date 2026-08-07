using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Research;

/// <summary>
/// The claim is where [[CONTEXT]] invariant 5 lives (T01 done-when 1 and 4, AC-02, AC-03). A
/// <see cref="ResearchClaim"/> cannot be constructed without a <see cref="ResearchSource"/> — an uncited
/// claim is unrepresentable, not merely rejected — and it copies its observed date from that source
/// rather than accepting one independently, so a claim can never claim to be fresher than the document it
/// rests on.
/// </summary>
public sealed class ResearchClaimTests
{
    private static readonly Guid ClaimId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
    private static readonly Guid SourceId = Guid.Parse("00000000-0000-0000-0000-0000000000F1");
    private static readonly DateTimeOffset ObservedAt = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);

    private static ResearchSource NewSource() =>
        new(SourceId, ResearchCategory.Layoffs, "https://news.example.com/layoffs", "Cuts", 900, ObservedAt);

    private static ResearchClaim NewClaim(
        ResearchSource? source = null,
        string claim = "Announced a 10% workforce reduction in Q4.",
        bool isWarning = true) =>
        new(ClaimId, source ?? NewSource(), ResearchCategory.Layoffs, claim, isWarning);

    [Fact]
    public void A_valid_claim_exposes_its_fields()
    {
        var claim = NewClaim();

        claim.Id.ShouldBe(ClaimId);
        claim.SourceId.ShouldBe(SourceId);
        claim.Category.ShouldBe(ResearchCategory.Layoffs);
        claim.Claim.ShouldBe("Announced a 10% workforce reduction in Q4.");
        claim.IsWarning.ShouldBeTrue();
    }

    [Fact]
    public void A_claim_without_a_source_is_unrepresentable()
    {
        // Invariant 5 as a type-level property: no source object, no claim — you cannot even call the ctor.
        Should.Throw<ArgumentNullException>(() =>
            new ResearchClaim(ClaimId, null!, ResearchCategory.News, "Something happened.", isWarning: false));
    }

    [Fact]
    public void The_observed_date_is_copied_from_the_source()
    {
        var source = NewSource();

        var claim = NewClaim(source: source);

        claim.ObservedAt.ShouldBe(source.ObservedAt);
    }

    [Fact]
    public void A_claim_carries_its_source_category_independently_of_the_source()
    {
        // The claim records the category it belongs to; it need not equal the fetching source's category
        // (a news document can substantiate a layoffs claim), so the claim's own category is authoritative.
        var source = new ResearchSource(SourceId, ResearchCategory.News, "https://news.example.com/x", "X", 500, ObservedAt);

        var claim = new ResearchClaim(ClaimId, source, ResearchCategory.Layoffs, "Layoffs reported.", isWarning: true);

        claim.Category.ShouldBe(ResearchCategory.Layoffs);
    }

    [Fact]
    public void A_claim_without_an_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            new ResearchClaim(Guid.Empty, NewSource(), ResearchCategory.Layoffs, "x", isWarning: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_claim_text_is_rejected(string text)
    {
        Should.Throw<ArgumentException>(() => NewClaim(claim: text));
    }
}
