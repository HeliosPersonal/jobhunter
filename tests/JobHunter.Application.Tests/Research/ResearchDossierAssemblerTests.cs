using JobHunter.Application.Research;
using JobHunter.Application.Tests.Support;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Research;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Research;

/// <summary>
/// T08: the dossier assembler is the deterministic core that turns what the fetchers found and what the
/// synthesiser returned into a <see cref="CompanyResearch"/> aggregate — storing every fetched document as a
/// source first, verifying each claim's cited URL against that set (T07), dropping and counting the ones the
/// model invented, and recording the categories no surviving claim speaks to as unavailable (AC-07). Warnings
/// ordering and the "every claim rests on a recorded source" invariant are the aggregate's job; the assembler's
/// job is to feed it only verified material.
/// </summary>
public sealed class ResearchDossierAssemblerTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000BB");
    private static readonly DateTimeOffset Observed = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Generated = new(2026, 8, 2, 6, 0, 0, TimeSpan.Zero);

    private static ResearchDossierAssembler NewAssembler(out CapturingLogger<ClaimVerifier> logger)
    {
        logger = new CapturingLogger<ClaimVerifier>();
        return new ResearchDossierAssembler(new ClaimVerifier(logger), new SequentialIdGenerator());
    }

    private static FetchedCategoryDocument Doc(ResearchCategory category, string url, string text = "body") =>
        new(category, new FetchedDocument(url, "title", text, Observed));

    private static ResearchDossierInput Input(
        IReadOnlyList<FetchedCategoryDocument> documents,
        IReadOnlyList<UnverifiedClaim> claims,
        string summary = "A short honest summary of the company.",
        CompanyStage? stage = null,
        string? employeeBand = null) =>
        new(
            CompanyId,
            RunId,
            documents,
            new ResearchSynthesis(summary, claims, stage, employeeBand),
            "research-v2",
            Generated);

    [Fact]
    public void It_stores_every_fetched_document_as_a_source()
    {
        var assembler = NewAssembler(out _);

        var dossier = assembler.Assemble(Input(
            [Doc(ResearchCategory.EngineeringBlog, "https://acme.ai/blog"), Doc(ResearchCategory.OpenSource, "https://api.github.com/orgs/acme/repos")],
            []));

        dossier.Sources.Count.ShouldBe(2);
        var blog = dossier.Sources.Single(s => s.Category == ResearchCategory.EngineeringBlog);
        blog.Url.ShouldBe("https://acme.ai/blog");
        blog.ObservedAt.ShouldBe(Observed);
        blog.TextLength.ShouldBe("body".Length);
    }

    [Fact]
    public void A_verified_claim_becomes_a_stored_claim_resting_on_its_source()
    {
        var assembler = NewAssembler(out _);

        var dossier = assembler.Assemble(Input(
            [Doc(ResearchCategory.EngineeringBlog, "https://acme.ai/blog")],
            [new UnverifiedClaim(ResearchCategory.EngineeringBlog, "They write about Rust.", "https://acme.ai/blog", IsWarning: false)]));

        dossier.Claims.Count.ShouldBe(1);
        var claim = dossier.Claims[0];
        claim.Category.ShouldBe(ResearchCategory.EngineeringBlog);
        claim.Claim.ShouldBe("They write about Rust.");
        claim.SourceId.ShouldBe(dossier.Sources.Single(s => s.Url == "https://acme.ai/blog").Id);
        claim.ObservedAt.ShouldBe(Observed);
    }

    [Fact]
    public void A_claim_citing_an_unfetched_url_is_discarded_and_counted()
    {
        var assembler = NewAssembler(out var logger);

        var dossier = assembler.Assemble(Input(
            [Doc(ResearchCategory.EngineeringBlog, "https://acme.ai/blog")],
            [
                new UnverifiedClaim(ResearchCategory.EngineeringBlog, "Real claim.", "https://acme.ai/blog", IsWarning: false),
                new UnverifiedClaim(ResearchCategory.Funding, "Invented claim.", "https://acme.ai/press/series-z", IsWarning: false),
            ]));

        dossier.Claims.Count.ShouldBe(1);
        dossier.ClaimsDiscarded.ShouldBe(1);
        logger.Entries.ShouldContain(e => e.Message.Contains("https://acme.ai/press/series-z"));
    }

    [Fact]
    public void Warnings_are_ordered_ahead_of_the_rest()
    {
        var assembler = NewAssembler(out _);

        var dossier = assembler.Assemble(Input(
            [Doc(ResearchCategory.EngineeringBlog, "https://acme.ai/blog"), Doc(ResearchCategory.Layoffs, "https://news.example/acme-layoffs")],
            [
                new UnverifiedClaim(ResearchCategory.EngineeringBlog, "They write about Rust.", "https://acme.ai/blog", IsWarning: false),
                new UnverifiedClaim(ResearchCategory.Layoffs, "They cut 10% of staff.", "https://news.example/acme-layoffs", IsWarning: true),
            ]));

        dossier.Claims[0].IsWarning.ShouldBeTrue();
    }

    [Fact]
    public void Categories_with_no_surviving_claim_are_recorded_unavailable()
    {
        var assembler = NewAssembler(out _);

        var dossier = assembler.Assemble(Input(
            [Doc(ResearchCategory.EngineeringBlog, "https://acme.ai/blog")],
            [new UnverifiedClaim(ResearchCategory.EngineeringBlog, "They write about Rust.", "https://acme.ai/blog", IsWarning: false)]));

        dossier.CategoriesCovered.ShouldBe([ResearchCategory.EngineeringBlog]);
        dossier.CategoriesUnavailable.ShouldNotContain(ResearchCategory.EngineeringBlog);
        // Every one of the eight that produced no surviving claim is named, not omitted (AC-07).
        foreach (var category in Enum.GetValues<ResearchCategory>().Where(c => c != ResearchCategory.EngineeringBlog))
        {
            dossier.CategoriesUnavailable.ShouldContain(category);
        }
    }

    [Fact]
    public void A_category_whose_only_claim_was_discarded_is_unavailable()
    {
        var assembler = NewAssembler(out _);

        var dossier = assembler.Assemble(Input(
            [Doc(ResearchCategory.EngineeringBlog, "https://acme.ai/blog")],
            [new UnverifiedClaim(ResearchCategory.Funding, "Invented.", "https://acme.ai/press/invented", IsWarning: false)]));

        dossier.CategoriesCovered.ShouldBeEmpty();
        dossier.CategoriesUnavailable.ShouldContain(ResearchCategory.Funding);
    }

    [Fact]
    public void It_stamps_the_identity_run_prompt_version_and_generation_instant()
    {
        var assembler = NewAssembler(out _);

        var dossier = assembler.Assemble(Input(
            [Doc(ResearchCategory.EngineeringBlog, "https://acme.ai/blog")],
            [],
            summary: "Header sentence."));

        dossier.CompanyId.ShouldBe(CompanyId);
        dossier.RunId.ShouldBe(RunId);
        dossier.Summary.ShouldBe("Header sentence.");
        dossier.PromptVersion.ShouldBe("research-v2");
        dossier.GeneratedAt.ShouldBe(Generated);
        dossier.Id.ShouldNotBe(Guid.Empty);
    }
}
