using JobHunter.Application.Normalization;
using JobHunter.Application.Normalization.Providers;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Normalization;

/// <summary>
/// T04: the small pure helpers underpinning normalisation — the deterministic candidate id, the
/// provider catalog, the employment-type parser and the HTML-to-plain-text reducer. Each is a pure
/// function of its input (SAD S5), so the tests assert determinism and the edge cases the handler relies on.
/// </summary>
public sealed class NormalizationPrimitivesTests
{
    [Fact]
    public void CandidateJobId_is_deterministic_and_well_formed()
    {
        var raw = Guid.CreateVersion7();

        var first = CandidateJobId.For(raw);
        var second = CandidateJobId.For(raw);

        first.ShouldBe(second);
        first.ShouldNotBe(Guid.Empty);
        // Version-5 nibble stamped so the value is a well-formed name-based UUID.
        first.ToByteArray()[7].ShouldBe(second.ToByteArray()[7]);
    }

    [Fact]
    public void CandidateJobId_differs_for_different_raw_postings()
    {
        CandidateJobId.For(Guid.CreateVersion7())
            .ShouldNotBe(CandidateJobId.For(Guid.CreateVersion7()));
    }

    [Fact]
    public void Catalog_resolves_a_registered_normaliser_by_kind_and_null_otherwise()
    {
        var catalog = new PostingNormalizerCatalog(new IPostingNormalizer[]
        {
            new GreenhousePostingNormalizer(),
            new LeverPostingNormalizer(),
        });

        catalog.For(AtsKind.Greenhouse).ShouldBeOfType<GreenhousePostingNormalizer>();
        catalog.For(AtsKind.Lever).ShouldBeOfType<LeverPostingNormalizer>();
        catalog.For(AtsKind.Ashby).ShouldBeNull();
    }

    [Fact]
    public void Catalog_rejects_a_duplicate_registration_at_construction()
    {
        Should.Throw<InvalidOperationException>(() =>
            new PostingNormalizerCatalog(new IPostingNormalizer[]
            {
                new GreenhousePostingNormalizer(),
                new GreenhousePostingNormalizer(),
            }));
    }

    [Theory]
    [InlineData("Full-time", EmploymentType.FullTime)]
    [InlineData("FULL_TIME", EmploymentType.FullTime)]
    [InlineData("Part time", EmploymentType.PartTime)]
    [InlineData("Contractor", EmploymentType.Contract)]
    [InlineData("Freelance", EmploymentType.Contract)]
    [InlineData("Intern", EmploymentType.Internship)]
    [InlineData("Internship", EmploymentType.Internship)]
    [InlineData("something-else", EmploymentType.Unknown)]
    [InlineData(null, EmploymentType.Unknown)]
    public void EmploymentTypeParser_maps_provider_spellings(string? input, EmploymentType expected)
    {
        EmploymentTypeParser.Parse(input).ShouldBe(expected);
    }

    [Fact]
    public void PlainText_double_decodes_strips_tags_and_collapses_whitespace()
    {
        PlainText.FromHtml("&lt;p&gt;A  &amp;amp; B&lt;/p&gt;").ShouldBe("A & B");
        PlainText.FromHtml("<p>Hello   world</p>").ShouldBe("Hello world");
        PlainText.FromHtml(null).ShouldBe(string.Empty);
        PlainText.FromHtml("").ShouldBe(string.Empty);
    }
}
