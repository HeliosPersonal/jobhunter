using System.Net;
using System.Net.Http.Json;
using JobHunter.Api.Endpoints;
using JobHunter.Domain.Companies;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The company endpoints end-to-end (T06): the detail keyed by canonical domain carries the registry
/// identity, its live ATS bindings and its currently-open jobs, with the F8-owned research dossier a null
/// slot until F8 merges (never fabricated, invariant 5); the Owner-only add refuses a duplicate with a
/// 409 and a malformed domain with a 400. The read route requires <c>jobhunter:read</c> and the write
/// route <c>jobhunter:admin</c>; a read token on the write route is a 403 (AC-07). No response carries a
/// CV-derived value, match reason or application note, nor the verbatim binding evidence (QG-2).
/// </summary>
public sealed class CompanyEndpointTests : IClassFixture<EndpointsHostFactory>
{
    private readonly EndpointsHostFactory _factory;

    public CompanyEndpointTests(EndpointsHostFactory factory) => _factory = factory;

    // --- Detail ------------------------------------------------------------------------------------

    [Fact]
    public async Task Company_detail_returns_identity_bindings_and_live_jobs_with_a_null_dossier()
    {
        var companyId = Guid.NewGuid();
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns(JobTestData.Company(companyId));
        _factory.Companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>())
            .Returns([JobTestData.Binding(companyId)]);
        _factory.CompanyJobs.LiveForCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns([JobTestData.LiveJob(Guid.NewGuid(), JobTestData.Seen)]);

        using var client = _factory.OwnerClient();
        var body = await client.GetFromJsonAsync<CompanyDto>(new Uri("/api/companies/snowflake.com", UriKind.Relative));

        body.ShouldNotBeNull();
        body.Name.ShouldBe("Snowflake");
        body.Domain.ShouldBe("snowflake.com");
        body.Source.ShouldBe("Curated");
        body.Bindings.Count.ShouldBe(1);
        body.Bindings[0].AtsKind.ShouldBe("Greenhouse");
        body.Bindings[0].BoardToken.ShouldBe("snowflake");
        body.Bindings[0].Confidence.ShouldBe(0.95m);
        body.LiveJobs.Count.ShouldBe(1);
        // The research dossier is F8-owned; until it merges the company carries no dossier, modelled as null.
        body.Research.ShouldBeNull();
    }

    [Fact]
    public async Task Company_detail_response_exposes_no_binding_evidence_or_cv_fields()
    {
        var companyId = Guid.NewGuid();
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns(JobTestData.Company(companyId));
        _factory.Companies.LiveBindingsAsync(companyId, Arg.Any<CancellationToken>())
            .Returns([JobTestData.Binding(companyId)]);
        _factory.CompanyJobs.LiveForCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns([]);

        using var client = _factory.OwnerClient();
        var raw = await client.GetStringAsync(new Uri("/api/companies/snowflake.com", UriKind.Relative));

        foreach (var forbidden in new[] { "evidence", "detector", "matchReason", "missingSkill", "cv" })
        {
            raw.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }

    [Fact]
    public async Task Company_detail_for_an_unknown_domain_is_a_404()
    {
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns((Company?)null);

        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/companies/nowhere.com", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Company_detail_for_a_malformed_domain_is_a_400()
    {
        using var client = _factory.OwnerClient();
        var response = await client.GetAsync(new Uri("/api/companies/not-a-domain", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Company_detail_requires_a_read_token()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/companies/snowflake.com", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Add ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Adding_a_company_creates_it_inactive_and_returns_201()
    {
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns((Company?)null);
        Company? added = null;
        await _factory.Companies.AddAsync(Arg.Do<Company>(c => added = c), Arg.Any<CancellationToken>());

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri("/api/companies", UriKind.Relative),
            new AddCompanyRequest("https://Datadog.com/careers", "Datadog", null, "US"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldContain("datadog.com");
        added.ShouldNotBeNull();
        added.CanonicalDomain.Value.ShouldBe("datadog.com");
        added.IsActive.ShouldBeFalse();
        added.Source.ShouldBe(CompanySource.Manual);
        await _factory.Companies.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Adding_a_company_that_already_exists_is_a_409()
    {
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns(JobTestData.Company(Guid.NewGuid()));

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri("/api/companies", UriKind.Relative),
            new AddCompanyRequest("snowflake.com", "Snowflake", null, "US"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Adding_a_company_with_a_malformed_domain_is_a_400()
    {
        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri("/api/companies", UriKind.Relative),
            new AddCompanyRequest("not-a-domain", "Nowhere", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Adding_a_company_with_a_blank_display_name_is_a_400()
    {
        _factory.Companies.FindByDomainAsync(Arg.Any<CanonicalDomain>(), Arg.Any<CancellationToken>())
            .Returns((Company?)null);

        using var client = _factory.OwnerClient("jobhunter:admin");
        var response = await client.PostAsJsonAsync(
            new Uri("/api/companies", UriKind.Relative),
            new AddCompanyRequest("datadog.com", "   ", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Adding_a_company_with_only_a_read_token_is_a_403()
    {
        using var client = _factory.OwnerClient();
        var response = await client.PostAsJsonAsync(
            new Uri("/api/companies", UriKind.Relative),
            new AddCompanyRequest("datadog.com", "Datadog", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Adding_a_company_requires_a_token()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            new Uri("/api/companies", UriKind.Relative),
            new AddCompanyRequest("datadog.com", "Datadog", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private sealed record CompanyDto(
        Guid Id,
        string Name,
        string Domain,
        string? Stage,
        string? HqCountry,
        string Source,
        bool IsActive,
        long FirstSeenAt,
        long LastSeenAt,
        IReadOnlyList<BindingDto> Bindings,
        IReadOnlyList<SummaryDto> LiveJobs,
        object? Research);

    private sealed record BindingDto(string AtsKind, string BoardToken, decimal Confidence, long DetectedAt);

    private sealed record SummaryDto(Guid Id, string Title, long FirstSeenAt, long LastSeenAt);
}
