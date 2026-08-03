namespace JobHunter.Api.Endpoints;

/// <summary>
/// The HTTP response shapes for the search and job endpoints (T05). They are hand-written DTOs, not the
/// domain aggregates: exactly like the <see cref="JobHunter.Domain.Search.JobDocument"/> allowlist, what
/// crosses the API boundary is stated field-by-field here and nowhere is a CV-derived value, a match
/// reason, a missing-skill list or an application note present (QG-2). The enrichment/score/application
/// members F3/F4/F6 own are modelled as nullable and populated as their absent value until those
/// features merge (the F9 cross-feature decoupling decision).
/// </summary>
public sealed record SearchHitResponse(
    string Id,
    string Title,
    string CompanyName,
    string CompanyDomain,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Countries,
    string RemotePolicy,
    string? Seniority,
    string EmploymentType,
    string? CompanyStage,
    string? AiUsage,
    int? SalaryMin,
    int? SalaryMax,
    string? SalaryCurrency,
    double Score,
    long? PostedAt,
    long FirstSeenAt,
    string Status,
    string? ApplicationStatus,
    string? Highlight);

/// <summary>One facet value and how many documents in the result carry it (AC-02).</summary>
public sealed record FacetCountResponse(string Value, int Count);

/// <summary>
/// A page of search results: the hits, the total <c>found</c> count, the per-field facet counts so a
/// client can offer refinements with no second round trip, the next opaque cursor (null at the end), and
/// the <c>partial</c> flag reported as-is when the provider degraded under load (never silently
/// truncated).
/// </summary>
public sealed record SearchResponse(
    IReadOnlyList<SearchHitResponse> Hits,
    int Found,
    IReadOnlyDictionary<string, IReadOnlyList<FacetCountResponse>> Facets,
    string? NextCursor,
    bool Partial);

/// <summary>The employer a job belongs to, as much as the registry knows (never CV-derived).</summary>
public sealed record CompanyRef(
    string Name,
    string Domain,
    string? Stage,
    string? HqCountry);

/// <summary>A place a job may be worked, as published (display form only, never the comparison key).</summary>
public sealed record JobLocationResponse(string Country, string? Region, string? City);

/// <summary>A published pay range on a job's detail — decimal amounts, explicit currency and period.</summary>
public sealed record SalaryResponse(decimal Min, decimal Max, string Currency, string Period);

/// <summary>A deterministic, vocabulary-matched technology tag and how it was matched.</summary>
public sealed record JobTechnologyResponse(string Technology, string MatchedVia);

/// <summary>
/// The full detail of one job (<c>GET /api/jobs/{id}</c>). The <c>score</c> is the API-side expression of
/// F4's explainability guarantee — its components reconcile to the total once F4 lands; until then a job
/// carries no ranking and the field is null (the decoupling decision). No enrichment, match reason or
/// application note is present here (QG-2).
/// </summary>
public sealed record JobDetailResponse(
    Guid Id,
    string Title,
    string Description,
    string Status,
    CompanyRef? Company,
    string? Seniority,
    string RemotePolicy,
    string EmploymentType,
    string ApplyUrl,
    IReadOnlyList<JobLocationResponse> Locations,
    IReadOnlyList<JobTechnologyResponse> Technologies,
    SalaryResponse? Salary,
    string? SalaryRaw,
    long? PostedAt,
    long FirstSeenAt,
    long LastSeenAt,
    long? ClosedAt,
    bool IsTier2,
    double? Score);

/// <summary>
/// One provenance row on <c>GET /api/jobs/{id}/aliases</c>: a raw posting that merged into the job, the
/// source it came from and the window it was seen over. This is the evidence for inspecting a suspected
/// bad merge without database access (F2 AC-08).
/// </summary>
public sealed record JobAliasResponse(
    Guid RawPostingId,
    Guid SourceId,
    long FirstSeenAt,
    long LastSeenAt);

/// <summary>A compact live-job summary for the cursor-paged <c>GET /api/jobs</c> list.</summary>
public sealed record JobSummaryResponse(
    Guid Id,
    string Title,
    string? Seniority,
    string RemotePolicy,
    string EmploymentType,
    string ApplyUrl,
    long FirstSeenAt,
    long LastSeenAt);

/// <summary>A page of the recent-jobs list, with the next opaque cursor (null at the end).</summary>
public sealed record JobsListResponse(
    IReadOnlyList<JobSummaryResponse> Jobs,
    string? NextCursor);

/// <summary>
/// One live ATS binding on a company's detail (<c>GET /api/companies/{domain}</c>): where the company's
/// jobs actually live and how strongly detection believes it (F2 §ats_bindings). The verbatim evidence
/// JSON is deliberately not exposed on the read surface — it is an internal debugging artefact, not a
/// public field; only the provider, board token and confidence are shown.
/// </summary>
public sealed record AtsBindingResponse(
    string AtsKind,
    string BoardToken,
    decimal Confidence,
    long DetectedAt);

/// <summary>
/// The full detail of one company (<c>GET /api/companies/{domain}</c>): its registry identity, its live
/// ATS bindings and its currently-open jobs. The latest research dossier F8 owns is a nullable slot —
/// null until F8 merges (the F9 cross-feature decoupling decision) — and is never fabricated. No
/// CV-derived value, match reason or application note is present (QG-2).
/// </summary>
public sealed record CompanyDetailResponse(
    Guid Id,
    string Name,
    string Domain,
    string? Stage,
    string? HqCountry,
    string Source,
    bool IsActive,
    long FirstSeenAt,
    long LastSeenAt,
    IReadOnlyList<AtsBindingResponse> Bindings,
    IReadOnlyList<JobSummaryResponse> LiveJobs,
    CompanyResearchResponse? Research);

/// <summary>
/// The latest research dossier slot on a company's detail (F8-owned). Modelled here so the company
/// contract is complete, but populated as null until F8 merges — an uncited or absent dossier is never
/// invented (invariant 5, the decoupling decision).
/// </summary>
public sealed record CompanyResearchResponse(
    long ComputedAt,
    IReadOnlyList<CompanyClaimResponse> Claims);

/// <summary>One dossier claim with its mandatory source URL and the date it was asserted (invariant 5).</summary>
public sealed record CompanyClaimResponse(
    string Statement,
    string SourceUrl,
    long AsOf);

/// <summary>The request body for <c>POST /api/companies</c> — the Owner adds a company to the registry.</summary>
public sealed record AddCompanyRequest(
    string Domain,
    string DisplayName,
    string? CareersUrl,
    string? HqCountry);
