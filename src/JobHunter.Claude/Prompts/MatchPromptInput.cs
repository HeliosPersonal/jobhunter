namespace JobHunter.Claude.Prompts;

/// <summary>
/// The facts one match prompt is rendered from (match-schema §Prompt, User template). It carries the
/// candidate side <strong>by value</strong> — the CV text and the Owner's stated preferences — and the
/// role side, plus an optional <see cref="MatchEnrichmentFacts"/>. When the enrichment is absent the
/// enrichment-derived lines are omitted entirely rather than filled with <c>Unknown</c> (AC-09).
///
/// <para><see cref="CvText"/> is the only CV-bearing value in the whole feature that becomes part of a
/// string, and it does so exactly once — here, on the way into <see cref="MatchPrompt.Render"/>. This
/// record is created, passed by value, and released; it is never placed on a context object, a log scope
/// or an <c>Activity</c> tag (match-schema §CV handling rules 1–2, invariant: the CV crosses exactly one
/// boundary).</para>
///
/// <para>Every candidate-side value is stable per CV version and per Profile — none is per-item or
/// per-Run — so the CV block renders identically across every item in a matching batch, which is what
/// makes the shared prompt-cache prefix load-bearing for the cost model (match-schema §Prompt caching).</para>
/// </summary>
public sealed record MatchPromptInput(
    // --- Candidate (by value; stable per CV version and Profile) ---
    string CvText,
    decimal? SalaryFloor,
    string? SalaryFloorCurrency,
    string OwnerTimezoneBand,
    string EmploymentTypesOpenTo,
    // --- Role (per item; rendered after the cache breakpoint) ---
    string CompanyName,
    string Title,
    string? Seniority,
    string LocationSummary,
    string EmploymentType,
    string? PublishedSalary,
    string Description,
    // --- Enrichment-derived (null omits the enrichment lines entirely, AC-09) ---
    MatchEnrichmentFacts? Enrichment);

/// <summary>
/// The enrichment-derived facts a match prompt folds into the role block when they exist (match-schema
/// §Prompt). When the whole object is <c>null</c> the enrichment lines are omitted rather than filled with
/// <c>Unknown</c> (AC-09), and the resulting score is later multiplied by a 0.85 confidence factor.
/// </summary>
public sealed record MatchEnrichmentFacts(
    string CompanyStage,
    bool IsRemote,
    string TimezoneBand,
    bool IsContractorFriendly,
    string? EstimatedSalary,
    decimal? SalaryConfidence,
    string Technologies,
    string AiUsage);
