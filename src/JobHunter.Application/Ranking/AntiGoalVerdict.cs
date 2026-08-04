namespace JobHunter.Application.Ranking;

/// <summary>
/// The output of <see cref="AntiGoalClassifier.Classify"/> (T15): whether a role is one the Owner is
/// deliberately leaving, and — when it is — the reason naming the family. A non-anti-goal verdict carries a
/// null reason, so the two states are unrepresentable together. The reason is the accountability trail the
/// down-weight or suppression it drives must carry (invariant 4/11): a penalised or hidden job can always be
/// told why.
/// </summary>
/// <param name="IsAntiGoal">True when the role is on the anti-goal track (low AI usage, enterprise-CRUD family).</param>
/// <param name="Reason">The reason naming the family when anti-goal; null when not.</param>
public readonly record struct AntiGoalVerdict(bool IsAntiGoal, string? Reason)
{
    /// <summary>The verdict for an ordinary role: no penalty, no reason.</summary>
    public static AntiGoalVerdict None { get; } = new(false, null);
}
