using System.Net;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Reporting;
using JobHunter.Domain.Research;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The owner-scoped research endpoints (F8 T09 C3, SAD §6.2): the latest dossier of a company keyed by
/// canonical domain, and the Owner-only request that queues a company for the next research cycle. The read
/// route declares <c>jobhunter:read</c> and the write route <c>jobhunter:admin</c> explicitly; requesting or
/// reading research as anyone other than the Owner is refused (AC-09). Every rendered claim carries its
/// source URL and observed date (invariant 5, AC-02/AC-03) and warnings appear first (AC-04). No response
/// carries a CV-derived value.
/// </summary>
public sealed class CompanyResearchEndpointTests : IClassFixture<EndpointsHostFactory>
{
    private readonly EndpointsHostFactory _factory;

    public CompanyResearchEndpointTests(EndpointsHostFactory factory) => _factory = factory;

    private static readonly DateTimeOffset Generated = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    // --- Read the latest dossier -------------------------------------------------------------------

    [Fact]
    public async Task Research_returns_the_latest_dossier_with_cited_claims_warnings_first()
    {
        var companyId = Guid.NewGuid();
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns(JobTestData.Company(companyId));
        _factory.CompanyResearch.LatestForCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new ResearchDossierSnapshot(
                Summary: "A late-stage data-warehouse company.",
                GeneratedAt: Generated,
                Claims:
                [
                    new ResearchClaimFacts(
                        ResearchCategory.Layoffs, "Cut 5% of staff in Q2.", Generated.AddDays(-3),
                        "https://news.example.com/layoffs", IsWarning: true),
                    new ResearchClaimFacts(
                        ResearchCategory.Funding, "Raised a Series F.", Generated.AddDays(-30),
                        "https://funding.example.com/series-f", IsWarning: false),
                ],
                CategoriesUnavailable: [ResearchCategory.Reviews, ResearchCategory.News]));

        using var client = _factory.OwnerClient();
        var body = await client.GetFromJsonAsync<DossierDto>(
            new Uri("/api/companies/snowflake.com/research", UriKind.Relative));

        body.ShouldNotBeNull();
        body.Summary.ShouldBe("A late-stage data-warehouse company.");
        body.GeneratedAt.ShouldBe(Generated.ToUnixTimeSeconds());
        body.Claims.Count.ShouldBe(2);
        // Warnings first (AC-04).
        body.Claims[0].Category.ShouldBe("Layoffs");
        body.Claims[0].IsWarning.ShouldBeTrue();
        body.Claims[0].SourceUrl.ShouldBe("https://news.example.com/layoffs");
        body.Claims[0].ObservedAt.ShouldBe(Generated.AddDays(-3).ToUnixTimeSeconds());
        body.Claims[1].Category.ShouldBe("Funding");
        body.CategoriesUnavailable.ShouldBe(["Reviews", "News"]);
    }

    [Fact]
    public async Task Research_for_a_known_company_that_has_never_been_researched_is_a_404()
    {
        var companyId = Guid.NewGuid();
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns(JobTestData.Company(companyId));
        _factory.CompanyResearch.LatestForCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns((ResearchDossierSnapshot?)null);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/companies/snowflake.com/research", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Research_for_an_unknown_company_is_a_404()
    {
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns((Company?)null);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/companies/nowhere.com/research", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Research_for_a_malformed_domain_is_a_400()
    {
        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/companies/not-a-domain/research", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Research_read_response_carries_no_cv_fields()
    {
        var companyId = Guid.NewGuid();
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns(JobTestData.Company(companyId));
        _factory.CompanyResearch.LatestForCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new ResearchDossierSnapshot(
                "Summary.", Generated,
                [new ResearchClaimFacts(ResearchCategory.Funding, "Raised.", Generated, "https://x.example/1", false)],
                []));

        using var client = _factory.OwnerClient();
        var raw = await client.GetStringAsync(new Uri("/api/companies/snowflake.com/research", UriKind.Relative));

        foreach (var forbidden in new[] { "matchReason", "missingSkill", "cv" })
        {
            raw.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }

    [Fact]
    public async Task Research_read_requires_a_token()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/companies/snowflake.com/research", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Queue on-demand research ------------------------------------------------------------------

    [Fact]
    public async Task Requesting_research_queues_the_company_and_returns_202()
    {
        var companyId = Guid.NewGuid();
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns(JobTestData.Company(companyId));

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(
            new Uri("/api/companies/snowflake.com/research", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await _factory.ResearchRequests.Received(1)
            .EnqueueAsync(companyId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Requesting_research_for_an_unknown_company_is_a_404_and_queues_nothing()
    {
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns((Company?)null);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(
            new Uri("/api/companies/nowhere.com/research", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await _factory.ResearchRequests.DidNotReceive()
            .EnqueueAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Requesting_research_for_a_malformed_domain_is_a_400()
    {
        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsync(
            new Uri("/api/companies/not-a-domain/research", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Requesting_research_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsync(
            new Uri("/api/companies/snowflake.com/research", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Requesting_research_requires_a_token()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            new Uri("/api/companies/snowflake.com/research", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private sealed record DossierDto(
        long GeneratedAt,
        string Summary,
        IReadOnlyList<ClaimDto> Claims,
        IReadOnlyList<string> CategoriesUnavailable);

    private sealed record ClaimDto(string Category, string Claim, long ObservedAt, string SourceUrl, bool IsWarning);
}
