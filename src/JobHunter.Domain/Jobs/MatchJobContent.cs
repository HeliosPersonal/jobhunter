using JobHunter.Domain.Intelligence;

namespace JobHunter.Domain.Jobs;

/// <summary>
/// The full content one <em>match</em> prompt's role side is rendered from (data-model §jobs/§enrichments,
/// match-schema §Prompt). It is the job facts of <see cref="EnrichmentJobContent"/> plus the latest
/// <see cref="MatchEnrichmentContent"/> for the job in this Run — or <c>null</c> when the job has no
/// enrichment, in which case the enrichment-derived prompt lines are omitted entirely and the resulting
/// score is later discounted (AC-09). A job is never dropped for lacking an enrichment.
///
/// <para>Like <see cref="EnrichmentJobContent"/> it carries <strong>nothing about the Owner</strong>: the
/// candidate side of a match prompt is folded in only at the Claude boundary, from the active CV and
/// Profile, so this read model — and the query behind it — never touches CV content (invariant: the CV
/// crosses exactly one boundary, F4's match prompt).</para>
/// </summary>
public sealed record MatchJobContent(
    Guid JobId,
    string CompanyName,
    string CanonicalDomain,
    string Title,
    string? Seniority,
    string LocationSummary,
    string? PublishedSalary,
    string EmploymentType,
    string Description,
    MatchEnrichmentContent? Enrichment);

/// <summary>
/// The enrichment-derived facts a match prompt folds into its role block when they exist (match-schema
/// §Prompt). Sourced from the latest <see cref="Enrichment"/> for the job in the Run; when the whole object
/// is <c>null</c> the enrichment lines are omitted rather than filled with <c>Unknown</c> (AC-09).
/// </summary>
public sealed record MatchEnrichmentContent(
    CompanyStage CompanyStage,
    bool IsRemote,
    TimezoneBand TimezoneBand,
    bool IsContractorFriendly,
    SalaryEstimate? EstimatedSalary,
    IReadOnlyList<string> Technologies,
    AiUsageLevel AiUsage);
