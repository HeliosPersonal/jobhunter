namespace JobHunter.Claude.Enrichment;

/// <summary>
/// The facts one enrichment prompt is rendered from (enrichment-schema §Prompt, User template). It is a
/// plain projection of the job — company, title, location, published salary, employment type and the
/// posting text — and deliberately carries <strong>nothing about the Owner</strong>: an enrichment prompt
/// describes the job, not the fit, so the CV never enters it (SAD §2, invariant — the CV crosses exactly
/// one boundary, and it is F4's, not this one).
/// </summary>
public sealed record EnrichmentPromptInput(
    string CompanyName,
    string CanonicalDomain,
    string Title,
    string LocationSummary,
    string? PublishedSalary,
    string EmploymentType,
    string Description);
