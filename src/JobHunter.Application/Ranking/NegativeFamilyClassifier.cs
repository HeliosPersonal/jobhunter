using System.Globalization;
using JobHunter.Domain.Intelligence;

namespace JobHunter.Application.Ranking;

/// <summary>
/// The negative role-family classifier (TUNE-06/F4 T17, match-schema §Suppression). A <strong>static, pure</strong>
/// function of the role's <see cref="RoleFamily"/> against a configured negative set: it decides whether a role is
/// in a family the Owner is not targeting — research-adjacent or prompt-writing roles that a fit-plus-AI-usage
/// score could otherwise float into the top ten (career-alignment review §8). Like <see cref="AlignmentCalculator"/>,
/// <see cref="AntiGoalClassifier"/> and <see cref="ScoreCalculator"/> it takes only values and has no clock, culture
/// or ordering dependency, so its verdict is provable rather than asserted (QG-3).
///
/// <para>This is the <em>general</em> off-target-family filter that <see cref="AntiGoalClassifier"/> (T15)
/// deliberately left out. The two are complementary and, under the defaults, disjoint: T15's anti-goal predicate is
/// the narrow low-AI-usage-on-<see cref="RoleFamily.EnterpriseCrud"/> case, while T17's default negative set is
/// <c>{MlResearch, DataScience, PromptEng}</c> — families that are off the AI-platform / staff trajectory
/// <em>whatever</em> their AI usage. The set is a <see cref="RankingOptions"/> value, so the Owner can widen or
/// narrow it without a deploy; a role is negative purely because its family is a member, independent of fit or AI
/// usage.</para>
/// </summary>
public static class NegativeFamilyClassifier
{
    /// <summary>
    /// Classifies a role against the configured <paramref name="negativeFamilies"/>. Returns a negative verdict
    /// carrying a reason that names the family when the role is in the set, or <see cref="NegativeFamilyVerdict.None"/>
    /// otherwise. The reason is what makes the resulting penalty or suppression accountable (invariant 4/11). An empty
    /// set — the Owner opting the filter off entirely — makes every role non-negative.
    /// </summary>
    public static NegativeFamilyVerdict Classify(RoleFamily roleFamily, IReadOnlySet<RoleFamily> negativeFamilies)
    {
        ArgumentNullException.ThrowIfNull(negativeFamilies);

        if (!negativeFamilies.Contains(roleFamily))
        {
            return NegativeFamilyVerdict.None;
        }

        var reason = string.Create(
            CultureInfo.InvariantCulture, $"Not a target role family: {roleFamily}");
        return new NegativeFamilyVerdict(true, reason);
    }
}
