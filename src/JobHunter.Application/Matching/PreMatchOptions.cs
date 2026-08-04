using JobHunter.Domain.Jobs;

namespace JobHunter.Application.Matching;

/// <summary>
/// The pre-match filter tunables (ADR-F4-0003, PRD §6), bound and validated at startup (coding-standards
/// §options). The Owner's seniority and the two thresholds the PRD leaves as configuration — the seniority
/// floor gap and the salary-confidence cut. These are <em>configuration</em>, not model-controlled, and the
/// regret sampler (T13) is what tells us if the thresholds are wrong, not a deploy.
///
/// <para>The calibration bypass is <em>not</em> here: it is spelt <c>Run:MatchAllJobs</c> on
/// <see cref="Enrichment.RunOptions"/> (ADR-F4-0003), because "match everything so we can measure what the
/// filter would have hidden" is a property of the day's Run, not of the filter's arithmetic. The filter stays
/// a pure function; only the submission handler reads that flag and skips the gate.</para>
/// </summary>
public sealed class PreMatchOptions
{
    public const string SectionName = "PreMatch";

    /// <summary>The Owner's seniority, the reference the floor rule measures a job against (default Senior).</summary>
    public Seniority OwnerSeniority { get; init; } = Seniority.Senior;

    /// <summary>How many IC rungs below the Owner disqualifies a job as too junior (default 2).</summary>
    public int SeniorityFloorGap { get; init; } = 2;

    /// <summary>The salary-estimate confidence at or above which the floor rule may exclude (default 0.8).</summary>
    public decimal SalaryConfidenceThreshold { get; init; } = 0.80m;

    /// <summary>Projects the tunables the pure filter needs.</summary>
    public PreMatchSettings ToSettings() =>
        new(OwnerSeniority, SeniorityFloorGap, SalaryConfidenceThreshold);
}
