using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Search;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The hand-written maps from domain types to the API response DTOs (T05). Every mapping is explicit —
/// there is no reflection projection and no member that carries a CV-derived value, a match reason, a
/// missing-skill list or an application note (QG-2). Where an F3/F4/F6-owned field has not merged, it is
/// mapped to its absent value (null), never invented.
/// </summary>
internal static class ResponseMapping
{
    public static SearchResponse ToResponse(SearchResults results) => new(
        Hits: [.. results.Hits.Select(ToHit)],
        Found: results.Found,
        Facets: results.Facets.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<FacetCountResponse>)[.. kvp.Value.Select(f => new FacetCountResponse(f.Value, f.Count))],
            StringComparer.Ordinal),
        NextCursor: results.NextCursor,
        Partial: results.Partial);

    public static SearchHitResponse ToHit(SearchHit hit)
    {
        var d = hit.Document;
        return new SearchHitResponse(
            Id: d.Id,
            Title: d.Title,
            CompanyName: d.CompanyName,
            CompanyDomain: d.CompanyDomain,
            Technologies: d.Technologies,
            Countries: d.Countries,
            RemotePolicy: d.RemotePolicy,
            Seniority: d.Seniority,
            EmploymentType: d.EmploymentType,
            CompanyStage: d.CompanyStage,
            AiUsage: d.AiUsage,
            SalaryMin: d.SalaryMin,
            SalaryMax: d.SalaryMax,
            SalaryCurrency: d.SalaryCurrency,
            Score: d.Score,
            PostedAt: d.PostedAt,
            FirstSeenAt: d.FirstSeenAt,
            Status: d.Status,
            ApplicationStatus: d.ApplicationStatus,
            Highlight: hit.Highlight);
    }

    public static JobDetailResponse ToDetail(Job job, Company? company) => new(
        Id: job.Id,
        Title: job.Title,
        Description: job.Description,
        Status: job.Status.ToString(),
        Company: company is null ? null : ToCompanyRef(company),
        Seniority: job.Seniority?.ToString(),
        RemotePolicy: job.RemotePolicy.ToString(),
        EmploymentType: job.EmploymentType.ToString(),
        ApplyUrl: job.ApplyUrl,
        Locations: [.. job.Locations.Locations.Select(ToLocation)],
        Technologies: [.. job.Technologies.Select(t => new JobTechnologyResponse(t.Technology, t.MatchedVia.ToString()))],
        Salary: job.Salary is null ? null : ToSalary(job.Salary),
        SalaryRaw: job.SalaryRaw,
        PostedAt: job.PostedAt?.ToUnixTimeSeconds(),
        FirstSeenAt: job.FirstSeenAt.ToUnixTimeSeconds(),
        LastSeenAt: job.LastSeenAt.ToUnixTimeSeconds(),
        ClosedAt: job.ClosedAt?.ToUnixTimeSeconds(),
        IsTier2: job.IsTier2,
        // Score is owned by F4; until it merges a job carries no ranking. Modelled as null, never fabricated.
        Score: null);

    public static CompanyRef ToCompanyRef(Company company) => new(
        Name: company.DisplayName,
        Domain: company.CanonicalDomain.Value,
        Stage: company.Stage,
        HqCountry: company.HqCountry);

    public static JobLocationResponse ToLocation(JobLocation location) =>
        new(location.Country, location.Region, location.City);

    public static SalaryResponse ToSalary(SalaryRange salary) =>
        new(salary.Min, salary.Max, salary.Currency, salary.Period.ToString());

    public static JobAliasResponse ToAlias(JobAlias alias) => new(
        RawPostingId: alias.RawPostingId,
        SourceId: alias.SourceId,
        FirstSeenAt: alias.FirstSeenAt.ToUnixTimeSeconds(),
        LastSeenAt: alias.LastSeenAt.ToUnixTimeSeconds());

    public static CompanyDetailResponse ToCompanyDetail(
        Company company,
        IReadOnlyList<AtsBinding> bindings,
        IReadOnlyList<LiveJob> liveJobs) => new(
        Id: company.Id,
        Name: company.DisplayName,
        Domain: company.CanonicalDomain.Value,
        Stage: company.Stage,
        HqCountry: company.HqCountry,
        Source: company.Source.ToString(),
        IsActive: company.IsActive,
        FirstSeenAt: company.FirstSeenAt.ToUnixTimeSeconds(),
        LastSeenAt: company.LastSeenAt.ToUnixTimeSeconds(),
        Bindings: [.. bindings.Select(ToBinding)],
        LiveJobs: [.. liveJobs.Select(ToSummary)],
        // The dossier is owned by F8; until it merges a company carries no research. Modelled as null,
        // never fabricated — an uncited claim is discarded, not shown (invariant 5).
        Research: null);

    public static AtsBindingResponse ToBinding(AtsBinding binding) => new(
        AtsKind: binding.AtsKind.ToString(),
        BoardToken: binding.BoardToken,
        Confidence: binding.Confidence.Value,
        DetectedAt: binding.DetectedAt.ToUnixTimeSeconds());

    public static JobSummaryResponse ToSummary(LiveJob job) => new(
        Id: job.Id,
        Title: job.Title,
        Seniority: job.Seniority,
        RemotePolicy: job.RemotePolicy,
        EmploymentType: job.EmploymentType,
        ApplyUrl: job.ApplyUrl,
        FirstSeenAt: job.FirstSeenAt.ToUnixTimeSeconds(),
        LastSeenAt: job.LastSeenAt.ToUnixTimeSeconds());
}
