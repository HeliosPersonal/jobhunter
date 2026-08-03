namespace JobHunter.Domain.Jobs;

/// <summary>
/// The full content one enrichment prompt is rendered from (data-model §jobs, enrichment-schema §Prompt).
/// Unlike <see cref="LiveJob"/> — which carries only what routing needs — this read model carries the
/// posting <see cref="Description"/> and the company facts the prompt quotes, because the submission step
/// (T10) renders a prompt per job and prices it before deciding whether to submit at all (QG-2).
///
/// <para>It deliberately carries <strong>nothing about the Owner</strong>: an enrichment prompt describes
/// the job, not the fit, so the CV never enters this boundary (invariant — the CV crosses exactly one
/// boundary, and it is F4's, not F3's).</para>
/// </summary>
public sealed record EnrichmentJobContent(
    Guid JobId,
    string CompanyName,
    string CanonicalDomain,
    string Title,
    string LocationSummary,
    string? PublishedSalary,
    string EmploymentType,
    string Description);
