namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The facts ranking needs about one matched job (F4 SAD §6.2, data-model §scores). The read side of "the
/// matches, enrichments and first-seen timestamps a Run's ranking pass scores over": the model's fit
/// judgement (<see cref="MatchScore"/>), when the job was first seen (for freshness), whether an enrichment
/// backs it (for the confidence multiplier — AC-09), the enrichment's estimated pay (for the opt-in
/// salary-floor down-weight), and the enrichment's AI-usage and role-family signals (for the career-alignment
/// component — T14). It carries <strong>nothing about the Owner</strong>: the CV crosses exactly one
/// boundary, and it is not this one (F4 invariant).
///
/// <para><see cref="AiUsage"/> and <see cref="RoleFamily"/> default to the safe, lowest-alignment values —
/// <see cref="AiUsageLevel.None"/> and <see cref="RoleFamily.Other"/> — so a match with no backing
/// enrichment is scored at zero AI-usage and Tier-3 family rather than crashing or over-rewarded.</para>
/// </summary>
/// <param name="JobId">The matched job; the score's identity and the deterministic tie-break key.</param>
/// <param name="MatchScore">The model's 0–100 fit judgement from the current match.</param>
/// <param name="FirstSeenAt">When the job was first seen, for the freshness component.</param>
/// <param name="HasEnrichment">True when an enrichment backs the job; drives the confidence multiplier.</param>
/// <param name="EstimatedSalary">The enrichment's estimated pay, or null; used only by the opt-in floor rule.</param>
/// <param name="AiUsage">The enrichment's AI-usage level; an input to the alignment component (T14).</param>
/// <param name="RoleFamily">The enrichment's role-family classification; an input to the alignment component (T14).</param>
public sealed record RankingJob(
    Guid JobId,
    int MatchScore,
    DateTimeOffset FirstSeenAt,
    bool HasEnrichment,
    SalaryEstimate? EstimatedSalary,
    AiUsageLevel AiUsage = AiUsageLevel.None,
    RoleFamily RoleFamily = RoleFamily.Other);
