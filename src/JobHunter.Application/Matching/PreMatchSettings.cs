using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Matching;

/// <summary>
/// The explicit values the pure <see cref="PreMatchFilter"/> needs beyond the job and the Profile: the Owner's
/// own seniority (the reference the floor rule measures against) and the two tunable thresholds the PRD leaves
/// as configuration — how many rungs below the Owner is a hard exclusion, and the salary-estimate confidence at
/// or above which the floor rule may bite (PRD open decision; default: two levels, confidence ≥ 0.8).
///
/// <para>A value, not an options object: passing it keeps the filter a pure function whose determinism is
/// provable (QG-3). <see cref="Application.Matching.PreMatchOptions"/> is the startup-validated config that
/// produces one of these; the filter itself never sees the options type.</para>
/// </summary>
/// <param name="OwnerSeniority">The Owner's seniority, the reference the floor rule measures a job against.</param>
/// <param name="SeniorityFloorGap">How many rungs below the Owner disqualifies a job (default 2).</param>
/// <param name="SalaryConfidenceThreshold">The estimate confidence at or above which the salary floor bites.</param>
public readonly record struct PreMatchSettings(
    Seniority OwnerSeniority,
    int SeniorityFloorGap,
    decimal SalaryConfidenceThreshold);
