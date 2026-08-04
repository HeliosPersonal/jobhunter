namespace JobHunter.Application.Ranking;

/// <summary>
/// The output of <see cref="NegativeFamilyClassifier.Classify"/> (T17): whether a role is in the Owner's
/// configured negative role-family set — the off-target families (ML-research, data-science, prompt-engineering
/// by default) a fit-plus-AI-usage score could otherwise slip past — and, when it is, the reason naming the
/// family. A non-negative verdict carries a null reason, so the two states are unrepresentable together. The
/// reason is the accountability trail the down-weight or suppression it drives must carry (invariant 4/11): a
/// penalised or hidden job can always be told why.
/// </summary>
/// <param name="IsNegative">True when the role's family is in the configured negative set.</param>
/// <param name="Reason">The reason naming the family when negative; null when not.</param>
public readonly record struct NegativeFamilyVerdict(bool IsNegative, string? Reason)
{
    /// <summary>The verdict for a role in no negative family: no penalty, no reason.</summary>
    public static NegativeFamilyVerdict None { get; } = new(false, null);
}
