using JobHunter.Application.Ranking;
using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// T15: the pure anti-goal classifier (TUNE-02, match-schema §Suppression). It answers one question from two
/// enrichment signals — is this a role the Owner is deliberately leaving? — namely low AI usage
/// (<see cref="AiUsageLevel.None"/> or <see cref="AiUsageLevel.Low"/>, and the tolerant parser's
/// <see cref="AiUsageLevel.Unknown"/>) on the enterprise-CRUD family. Every anti-goal verdict carries a
/// reason naming the family (invariant 4/11), so the down-weight or suppression it drives is never a silent
/// filter. Like the rest of the ranking chain it is static and pure, so its verdict is provable (QG-3).
/// </summary>
public sealed class AntiGoalClassifierTests
{
    [Theory]
    [InlineData(AiUsageLevel.None)]
    [InlineData(AiUsageLevel.Low)]
    [InlineData(AiUsageLevel.Unknown)]
    public void Low_ai_usage_on_the_enterprise_crud_family_is_anti_goal(AiUsageLevel aiUsage)
    {
        var verdict = AntiGoalClassifier.Classify(aiUsage, RoleFamily.EnterpriseCrud);

        verdict.IsAntiGoal.ShouldBeTrue();
        verdict.Reason.ShouldBe("Anti-goal role family: EnterpriseCrud");
    }

    [Theory]
    [InlineData(AiUsageLevel.Medium)]
    [InlineData(AiUsageLevel.High)]
    public void Enterprise_crud_with_real_ai_usage_is_not_anti_goal(AiUsageLevel aiUsage)
    {
        // The guard is deliberately narrow: an enterprise-CRUD posting that genuinely involves AI work is not
        // the track the Owner is leaving, so it escapes the penalty and rides its alignment instead.
        var verdict = AntiGoalClassifier.Classify(aiUsage, RoleFamily.EnterpriseCrud);

        verdict.IsAntiGoal.ShouldBeFalse();
        verdict.Reason.ShouldBeNull();
    }

    [Theory]
    [InlineData(RoleFamily.AiPlatform)]
    [InlineData(RoleFamily.Platform)]
    [InlineData(RoleFamily.BackendGeneric)]
    [InlineData(RoleFamily.Frontend)]
    [InlineData(RoleFamily.MlResearch)]
    [InlineData(RoleFamily.Other)]
    public void A_low_ai_role_outside_the_enterprise_crud_family_is_not_anti_goal(RoleFamily roleFamily)
    {
        // T15 down-weights only the CRUD/traditional-enterprise family. A low-AI frontend or research role is
        // merely low-alignment (T14 already handles that) — the negative-family filter for the other
        // off-target families is T17, not this task.
        var verdict = AntiGoalClassifier.Classify(AiUsageLevel.None, roleFamily);

        verdict.IsAntiGoal.ShouldBeFalse();
        verdict.Reason.ShouldBeNull();
    }
}
