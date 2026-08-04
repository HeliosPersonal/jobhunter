using System.Globalization;
using JobHunter.Domain.Intelligence;

namespace JobHunter.Application.Ranking;

/// <summary>
/// The anti-goal classifier (TUNE-02/F4 T15, match-schema §Suppression). A <strong>static, pure</strong>
/// function of the two enrichment signals — <see cref="AiUsageLevel"/> and <see cref="RoleFamily"/> — that
/// decides whether a role is one the Owner is deliberately leaving: the fit-dominant scoring that would
/// otherwise float a Senior-.NET / enterprise-CRUD role into the top ten (career-alignment review §8). Like
/// <see cref="AlignmentCalculator"/> and <see cref="ScoreCalculator"/> it takes only values and has no clock,
/// culture or ordering dependency, so its verdict is provable rather than asserted (QG-3).
///
/// <para>The guard is deliberately narrow — it is a <em>down-weight of a specific anti-goal</em>, not the
/// general off-target-family filter (that is T17). A role is anti-goal only when its AI usage is
/// <see cref="AiUsageLevel.None"/> or <see cref="AiUsageLevel.Low"/> (and the tolerant parser's
/// <see cref="AiUsageLevel.Unknown"/>, which resolves to no evidence of AI) <em>and</em> its family is
/// <see cref="RoleFamily.EnterpriseCrud"/>. An enterprise-CRUD posting that genuinely involves AI work is
/// not the track being left, and a low-AI role in another family is merely low-alignment — T14 already
/// down-weights that through the <c>alignment</c> component.</para>
/// </summary>
public static class AntiGoalClassifier
{
    /// <summary>
    /// Classifies a role. Returns an anti-goal verdict carrying a reason that names the family when the role
    /// is on the anti-goal track, or <see cref="AntiGoalVerdict.None"/> otherwise. The reason is what makes
    /// the resulting penalty or suppression accountable (invariant 4/11).
    /// </summary>
    public static AntiGoalVerdict Classify(AiUsageLevel aiUsage, RoleFamily roleFamily)
    {
        if (roleFamily != RoleFamily.EnterpriseCrud || !IsLowAiUsage(aiUsage))
        {
            return AntiGoalVerdict.None;
        }

        var reason = string.Create(
            CultureInfo.InvariantCulture, $"Anti-goal role family: {roleFamily}");
        return new AntiGoalVerdict(true, reason);
    }

    /// <summary>
    /// Low AI usage: no AI content (<see cref="AiUsageLevel.None"/>), incidental tooling
    /// (<see cref="AiUsageLevel.Low"/>), or the tolerant parser's <see cref="AiUsageLevel.Unknown"/> — absence
    /// of evidence, treated as no AI. <see cref="AiUsageLevel.Medium"/> and above are real AI work.
    /// </summary>
    private static bool IsLowAiUsage(AiUsageLevel aiUsage) =>
        aiUsage is AiUsageLevel.None or AiUsageLevel.Low or AiUsageLevel.Unknown;
}
