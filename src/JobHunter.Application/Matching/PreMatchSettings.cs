using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Matching;

/// <summary>
/// The explicit values the pure <see cref="PreMatchFilter"/> needs beyond the job and the Profile: the Owner's
/// own seniority (the reference the floor rule measures against) and the tunable thresholds the PRD leaves as
/// configuration — how many rungs below the Owner is a hard exclusion, the salary-estimate confidence at or
/// above which the floor rule may bite (PRD open decision; default: two levels, confidence ≥ 0.8), and the set
/// of early company stages exempt from the seniority floor (T18; default <c>{Seed, SeriesA}</c>).
///
/// <para>A value, not an options object: passing it keeps the filter a pure function whose determinism is
/// provable (QG-3). <see cref="Application.Matching.PreMatchOptions"/> is the startup-validated config that
/// produces one of these; the filter itself never sees the options type.</para>
/// </summary>
/// <param name="OwnerSeniority">The Owner's seniority, the reference the floor rule measures a job against.</param>
/// <param name="SeniorityFloorGap">How many rungs below the Owner disqualifies a job (default 2).</param>
/// <param name="SalaryConfidenceThreshold">The estimate confidence at or above which the salary floor bites.</param>
/// <param name="SeniorityFloorExemptStages">Company stages whose erratic levelling exempts a role from the
/// seniority floor entirely (T18) — a Founding-Engineer / early-startup role the Owner wants is not dropped on
/// an absolute level gap. The exemption is evidence-driven: it needs an enrichment stage fact to apply.</param>
public readonly record struct PreMatchSettings(
    Seniority OwnerSeniority,
    int SeniorityFloorGap,
    decimal SalaryConfidenceThreshold,
    IReadOnlySet<CompanyStage> SeniorityFloorExemptStages);
