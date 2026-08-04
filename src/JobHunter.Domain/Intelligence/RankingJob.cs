namespace JobHunter.Domain.Intelligence;

/// <summary>
/// The facts ranking needs about one matched job (F4 SAD §6.2, data-model §scores). The read side of "the
/// matches, enrichments and first-seen timestamps a Run's ranking pass scores over": the model's fit
/// judgement (<see cref="MatchScore"/>), when the job was first seen (for freshness), whether an enrichment
/// backs it (for the confidence multiplier — AC-09), and the enrichment's estimated pay (for the opt-in
/// salary-floor down-weight). It carries <strong>nothing about the Owner</strong>: the CV crosses exactly
/// one boundary, and it is not this one (F4 invariant).
/// </summary>
/// <param name="JobId">The matched job; the score's identity and the deterministic tie-break key.</param>
/// <param name="MatchScore">The model's 0–100 fit judgement from the current match.</param>
/// <param name="FirstSeenAt">When the job was first seen, for the freshness component.</param>
/// <param name="HasEnrichment">True when an enrichment backs the job; drives the confidence multiplier.</param>
/// <param name="EstimatedSalary">The enrichment's estimated pay, or null; used only by the opt-in floor rule.</param>
public sealed record RankingJob(
    Guid JobId,
    int MatchScore,
    DateTimeOffset FirstSeenAt,
    bool HasEnrichment,
    SalaryEstimate? EstimatedSalary);
