using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Companies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The company endpoints (API contract §Companies, T06): the detail of one company keyed by its
/// canonical domain — its registry identity, live ATS bindings and currently-open jobs — and the
/// Owner-only <c>POST /api/companies</c> that adds a company to the registry. The read route declares
/// <c>jobhunter:read</c> and the write route <c>jobhunter:admin</c> explicitly (the endpoint-convention
/// gate); reads come through Dapper/EF ports, never the index (SAD §4 S5). The research dossier F8 owns
/// is a nullable slot on the response, null until F8 merges (the decoupling decision), never fabricated
/// (invariant 5).
/// </summary>
public static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/companies/{domain}", HandleDetailAsync)
            .WithName("CompanyDetail")
            .WithSummary("A company keyed by canonical domain: identity, ATS bindings and live jobs.")
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        app.MapPost("/api/companies", HandleAddAsync)
            .WithName("AddCompany")
            .WithSummary("Adds a company to the registry (Owner only).")
            .RequireAuthorization(ApiSecurityExtensions.AdminPolicy);

        return app;
    }

    internal static async Task<IResult> HandleDetailAsync(
        string domain,
        ICompanyRepository companies,
        ICompanyJobsQuery companyJobs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(companyJobs);

        var canonical = CanonicalDomain.TryCreate(domain);
        if (canonical.IsFailure)
        {
            return Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "invalid-domain",
                title: "The company domain is not valid",
                detail: "The company domain is not a canonicalisable domain.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var company = await companies.FindByDomainAsync(canonical.Value, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return NotFound(canonical.Value.Value);
        }

        var bindings = await companies.LiveBindingsAsync(company.Id, cancellationToken).ConfigureAwait(false);
        var liveJobs = await companyJobs.LiveForCompanyAsync(company.Id, cancellationToken).ConfigureAwait(false);

        return Results.Ok(ResponseMapping.ToCompanyDetail(company, bindings, liveJobs));
    }

    internal static async Task<IResult> HandleAddAsync(
        AddCompanyRequest request,
        ICompanyRepository companies,
        IClock clock,
        IIdGenerator ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);

        var canonical = CanonicalDomain.TryCreate(request.Domain);
        if (canonical.IsFailure)
        {
            return Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "invalid-domain",
                title: "The company domain is not valid",
                detail: "The company domain is not a canonicalisable domain.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // A company added by hand is created inactive with no binding: activation is detection's job, so a
        // manual entry never leaks into the discovery fan-out until a confident binding is found (AC-04).
        var created = Company.TryCreate(
            ids.NewId(), canonical.Value, request.DisplayName, CompanySource.Manual, clock.UtcNow,
            request.CareersUrl, request.HqCountry, isActive: false);
        if (created.IsFailure)
        {
            return Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "invalid-company",
                title: "The company could not be created",
                detail: created.Error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var existing = await companies.FindByDomainAsync(canonical.Value, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            // At most one registry row per canonical domain: adding a company that already exists is a
            // conflict, not a silent second insert (the registry is keyed on the canonical domain).
            return Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "company-exists",
                title: "The company is already in the registry",
                detail: $"A company already exists for domain {canonical.Value.Value}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await companies.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await companies.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var body = ResponseMapping.ToCompanyDetail(created.Value, [], []);
        return Results.Created($"/api/companies/{canonical.Value.Value}", body);
    }

    private static IResult NotFound(string domain) => Results.Problem(
        type: SearchEndpoints.ErrorTypeBase + "not-found",
        title: "The requested company does not exist",
        detail: $"No company was found for domain {domain}.",
        statusCode: StatusCodes.Status404NotFound);
}
