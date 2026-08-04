namespace JobHunter.Application.Ranking;

/// <summary>
/// Tunables for the ranking stage (F4 SAD §8). Bound and validated at startup (coding-standards §options).
/// The number of top job ids carried on <c>RankingCompleted</c> for downstream consumers, and the Owner's
/// opt-in that turns the salary floor from a down-weight into a hard suppression rule (O5). The ranking
/// <em>weights</em> are a domain value (<c>RankingWeights.Default</c>), tunable without a deploy; they are not
/// duplicated here.
/// </summary>
public sealed class RankingOptions
{
    public const string SectionName = "Ranking";

    /// <summary>How many top job ids to carry on <c>RankingCompleted</c> — the digest shows ten cards.</summary>
    public int TopJobCount { get; init; } = 10;

    /// <summary>
    /// When true, a high-confidence salary estimate below the Owner's floor suppresses the job with a reason;
    /// off by default, because the floor is a down-weight, not a filter, until explicitly opted in (O5).
    /// </summary>
    public bool SalaryFloorSuppression { get; init; }

    /// <summary>
    /// The multiplier applied to the final score of a role classified anti-goal by
    /// <see cref="AntiGoalClassifier"/> — low AI usage on the enterprise-CRUD family (TUNE-02, T15). Must be in
    /// <c>[0,1]</c>; the default 0.5 halves the score so a high-fit anti-goal role no longer floats into the top
    /// ten purely on fit. A value of 1.0 disables the down-weight without a code change.
    /// </summary>
    public decimal AntiGoalPenaltyFactor { get; init; } = 0.5m;

    /// <summary>
    /// When true, a role classified anti-goal is suppressed with a reason rather than merely down-weighted; off
    /// by default, because the anti-goal down-weight is a penalty, not a filter, until explicitly opted in
    /// (invariant 11 — a suppressed job stays retrievable and counted in the footer).
    /// </summary>
    public bool AntiGoalSuppression { get; init; }
}
