using JobHunter.Domain.Research;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Research;

/// <summary>
/// <see cref="CompanyResearch"/> is one dossier per (company, run) (data-model §company_research). It
/// carries only cited claims — every claim it holds rests on one of its own recorded sources — and it
/// states which categories produced nothing, because absence of information is information (AC-07). The
/// aggregate has no dependency on HTTP, EF Core or Anthropic (T01 done-when 5): it is assembled from
/// values the Application layer has already fetched, verified and discarded.
/// </summary>
public sealed class CompanyResearchTests
{
    private static readonly Guid ResearchId = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000A2");
    private static readonly Guid SourceId = Guid.Parse("00000000-0000-0000-0000-0000000000F1");
    private static readonly Guid ClaimId = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
    private static readonly DateTimeOffset ObservedAt = new(2026, 1, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GeneratedAt = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static ResearchSource NewSource(Guid? id = null, ResearchCategory category = ResearchCategory.EngineeringBlog) =>
        new(id ?? SourceId, category, "https://example.com/blog", "Blog", 3000, ObservedAt);

    private static ResearchClaim NewClaim(ResearchSource source, ResearchCategory category = ResearchCategory.EngineeringBlog) =>
        new(ClaimId, source, category, "Runs a well-documented engineering blog.", isWarning: false);

    private static CompanyResearch NewResearch(
        IReadOnlyList<ResearchSource>? sources = null,
        IReadOnlyList<ResearchClaim>? claims = null,
        IReadOnlyList<ResearchCategory>? unavailable = null,
        int claimsDiscarded = 0,
        string summary = "A mid-stage company with an active engineering blog.")
    {
        var source = NewSource();
        return new CompanyResearch(
            ResearchId,
            CompanyId,
            RunId,
            summary,
            sources ?? [source],
            claims ?? [NewClaim(source)],
            unavailable ?? [ResearchCategory.Layoffs, ResearchCategory.Funding],
            claimsDiscarded,
            "research-v1",
            GeneratedAt);
    }

    [Fact]
    public void A_valid_dossier_exposes_its_fields()
    {
        var research = NewResearch();

        research.Id.ShouldBe(ResearchId);
        research.CompanyId.ShouldBe(CompanyId);
        research.RunId.ShouldBe(RunId);
        research.Summary.ShouldBe("A mid-stage company with an active engineering blog.");
        research.PromptVersion.ShouldBe("research-v1");
        research.GeneratedAt.ShouldBe(GeneratedAt);
        research.Sources.Count.ShouldBe(1);
        research.Claims.Count.ShouldBe(1);
    }

    [Fact]
    public void Covered_categories_are_derived_from_the_claims()
    {
        // AC-07: covered is not stored independently — it is exactly the categories the claims speak to,
        // so it can never disagree with what was actually asserted.
        var research = NewResearch();

        research.CategoriesCovered.ShouldBe([ResearchCategory.EngineeringBlog]);
    }

    [Fact]
    public void Unavailable_categories_are_recorded_explicitly()
    {
        var research = NewResearch(unavailable: [ResearchCategory.Layoffs]);

        research.CategoriesUnavailable.ShouldBe([ResearchCategory.Layoffs]);
    }

    [Fact]
    public void A_claim_resting_on_an_unrecorded_source_is_rejected()
    {
        // The whole design: a claim may only cite a source the dossier actually fetched and stored.
        var recorded = NewSource(id: SourceId);
        var phantom = NewSource(id: Guid.Parse("00000000-0000-0000-0000-0000000000F9"));

        Should.Throw<ArgumentException>(() =>
            NewResearch(sources: [recorded], claims: [NewClaim(phantom)]));
    }

    [Fact]
    public void A_dossier_may_hold_no_claims_when_every_category_was_unavailable()
    {
        // A company that yielded nothing is still a dossier — it says so, rather than being absent (AC-07).
        var research = NewResearch(
            sources: [],
            claims: [],
            unavailable: [ResearchCategory.News, ResearchCategory.Funding]);

        research.Claims.ShouldBeEmpty();
        research.CategoriesCovered.ShouldBeEmpty();
        research.CategoriesUnavailable.Count.ShouldBe(2);
    }

    [Fact]
    public void The_discarded_count_is_retained()
    {
        var research = NewResearch(claimsDiscarded: 3);

        research.ClaimsDiscarded.ShouldBe(3);
    }

    [Fact]
    public void A_negative_discarded_count_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewResearch(claimsDiscarded: -1));
    }

    [Fact]
    public void A_dossier_without_a_company_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            new CompanyResearch(ResearchId, Guid.Empty, RunId, "s", [], [], [], 0, "v1", GeneratedAt));
    }

    [Fact]
    public void A_dossier_without_a_run_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            new CompanyResearch(ResearchId, CompanyId, Guid.Empty, "s", [], [], [], 0, "v1", GeneratedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_summary_is_rejected(string summary)
    {
        Should.Throw<ArgumentException>(() => NewResearch(summary: summary));
    }

    [Fact]
    public void A_blank_prompt_version_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            new CompanyResearch(ResearchId, CompanyId, RunId, "s", [], [], [], 0, "  ", GeneratedAt));
    }

    [Fact]
    public void A_category_cannot_be_both_covered_and_unavailable()
    {
        // Contradiction guard: if a category produced a claim it is covered, so declaring it unavailable
        // too would let the dossier say "no blog found" while showing a blog claim.
        var source = NewSource(category: ResearchCategory.EngineeringBlog);

        Should.Throw<ArgumentException>(() => NewResearch(
            sources: [source],
            claims: [NewClaim(source, ResearchCategory.EngineeringBlog)],
            unavailable: [ResearchCategory.EngineeringBlog]));
    }

    [Fact]
    public void Warnings_come_before_non_warnings_in_the_claims_view()
    {
        // AC-04: layoffs/funding-difficulty warnings are surfaced first, so the aggregate orders them ahead.
        var source = NewSource(category: ResearchCategory.News);
        var informational = new ResearchClaim(
            Guid.Parse("00000000-0000-0000-0000-0000000000C2"), source, ResearchCategory.News, "Opened a new office.", isWarning: false);
        var warning = new ResearchClaim(
            Guid.Parse("00000000-0000-0000-0000-0000000000C3"), source, ResearchCategory.Layoffs, "Announced layoffs.", isWarning: true);

        var research = NewResearch(
            sources: [source],
            claims: [informational, warning],
            unavailable: []);

        research.Claims[0].IsWarning.ShouldBeTrue();
    }
}
