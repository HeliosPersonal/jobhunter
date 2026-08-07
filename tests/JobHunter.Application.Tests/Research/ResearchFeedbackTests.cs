using JobHunter.Application.Research;
using JobHunter.Application.Tests.Support;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Research;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Research;

/// <summary>
/// T08 C4: once a dossier is assembled, two things flow out of it — the firmographics the model classified
/// from the fetched text are fed back onto the <see cref="Company"/> record (AC-10), using the dossier's
/// generation instant as the observation so a re-run of an older dossier never overwrites a fresher one; and a
/// <see cref="Contracts.Pipeline.ResearchCompleted"/> event is minted carrying the count of <em>verified</em>
/// claims (a company that fetched nothing still completes, so the digest is never left in silence).
/// </summary>
public sealed class ResearchFeedbackTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-0000000000BB");
    private static readonly DateTimeOffset Observed = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Generated = new(2026, 8, 2, 6, 0, 0, TimeSpan.Zero);

    private static Company NewCompany() =>
        new(CompanyId, CanonicalDomain.TryCreate("acme.ai").Value, "Acme AI", CompanySource.Curated, Observed);

    private static CompanyResearch Assemble(
        IReadOnlyList<FetchedCategoryDocument> documents,
        IReadOnlyList<UnverifiedClaim> claims,
        CompanyStage? stage = null,
        string? employeeBand = null)
    {
        var assembler = new ResearchDossierAssembler(
            new ClaimVerifier(new CapturingLogger<ClaimVerifier>()), new SequentialIdGenerator());
        return assembler.Assemble(new ResearchDossierInput(
            CompanyId,
            RunId,
            documents,
            new ResearchSynthesis("A short honest summary.", claims, stage, employeeBand),
            "research-v2",
            Generated));
    }

    private static FetchedCategoryDocument Doc(ResearchCategory category, string url) =>
        new(category, new FetchedDocument(url, "title", "body", Observed));

    [Fact]
    public void It_feeds_firmographics_back_onto_the_company_at_the_generation_instant()
    {
        var company = NewCompany();

        var changed = ResearchFeedback.ApplyFirmographics(
            company,
            new ResearchSynthesis("s", [], CompanyStage.SeriesB, "51-200"),
            Generated);

        changed.ShouldBeTrue();
        company.Stage.ShouldBe("SeriesB");
        company.EmployeeBand.ShouldBe("51-200");
        company.FirmographicsObservedAt.ShouldBe(Generated);
    }

    [Fact]
    public void It_applies_no_firmographics_when_the_model_gave_none()
    {
        var company = NewCompany();

        var changed = ResearchFeedback.ApplyFirmographics(
            company, new ResearchSynthesis("s", [], null, null), Generated);

        changed.ShouldBeFalse();
        company.Stage.ShouldBeNull();
        company.FirmographicsObservedAt.ShouldBeNull();
    }

    [Fact]
    public void The_completed_event_carries_the_verified_claim_count()
    {
        var dossier = Assemble(
            [Doc(ResearchCategory.EngineeringBlog, "https://acme.ai/blog")],
            [
                new UnverifiedClaim(ResearchCategory.EngineeringBlog, "Real.", "https://acme.ai/blog", IsWarning: false),
                new UnverifiedClaim(ResearchCategory.Funding, "Invented.", "https://acme.ai/press/z", IsWarning: false),
            ]);

        var completed = ResearchFeedback.CompletedEvent(dossier, Generated);

        completed.RunId.ShouldBe(RunId);
        completed.CompanyId.ShouldBe(CompanyId);
        completed.ResearchId.ShouldBe(dossier.Id);
        completed.ClaimCount.ShouldBe(1);
        completed.OccurredAt.ShouldBe(Generated);
    }

    [Fact]
    public void An_empty_dossier_still_completes_with_a_zero_claim_count()
    {
        var dossier = Assemble([], []);

        var completed = ResearchFeedback.CompletedEvent(dossier, Generated);

        completed.ClaimCount.ShouldBe(0);
        completed.ResearchId.ShouldBe(dossier.Id);
    }
}
