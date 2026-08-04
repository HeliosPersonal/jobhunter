using JobHunter.Application.Ranking;
using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Ranking;

/// <summary>
/// The pure career-alignment component (TUNE-01/F4 T14). Like <see cref="ScoreCalculator"/> it is a
/// static function of explicit values — no clock, no repository — so its output is a fact these tests
/// assert. It maps the enrichment's <see cref="AiUsageLevel"/> and <see cref="RoleFamily"/> onto a
/// fraction in <c>[0,1]</c> that rewards the Owner's AI-platform / staff trajectory, and every result
/// carries a reason (invariant 4).
/// </summary>
public sealed class AlignmentCalculatorTests
{
    [Fact]
    public void A_tier_one_high_ai_usage_role_is_full_alignment()
    {
        // The two named endpoints of the design (T14 done-when).
        var result = AlignmentCalculator.Calculate(AiUsageLevel.High, RoleFamily.AiPlatform);

        result.Value.ShouldBe(1.0m);
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_no_ai_usage_anti_goal_role_is_zero_alignment()
    {
        var result = AlignmentCalculator.Calculate(AiUsageLevel.None, RoleFamily.EnterpriseCrud);

        result.Value.ShouldBe(0.0m);
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Every_result_carries_a_reason_naming_both_signals()
    {
        // Invariant 4: an unexplained component never reaches the Owner. The reason names the role family
        // and the AI-usage level it was blended from, so the number is always accountable.
        var result = AlignmentCalculator.Calculate(AiUsageLevel.Medium, RoleFamily.Platform);

        result.Reason.ShouldContain("Platform");
        result.Reason.ShouldContain("Medium");
    }

    [Theory]
    [InlineData(AiUsageLevel.None, 0.0)]
    [InlineData(AiUsageLevel.Low, 0.25)]
    [InlineData(AiUsageLevel.Medium, 0.6)]
    [InlineData(AiUsageLevel.High, 1.0)]
    public void Ai_usage_maps_to_the_specified_scores_holding_role_family_fixed(
        AiUsageLevel usage, double aiScore)
    {
        // With a Tier-1 family (tier score 1.0), the blend is 0.5·aiScore + 0.5·1.0.
        var result = AlignmentCalculator.Calculate(usage, RoleFamily.AiPlatform);

        result.Value.ShouldBe((decimal)(0.5 * aiScore + 0.5 * 1.0), tolerance: 0.0001m);
    }

    [Theory]
    [InlineData(RoleFamily.AiPlatform, 1.0)]
    [InlineData(RoleFamily.Platform, 1.0)]
    [InlineData(RoleFamily.AiApplications, 1.0)]
    [InlineData(RoleFamily.ForwardDeployed, 1.0)]
    [InlineData(RoleFamily.FoundingEng, 1.0)]
    [InlineData(RoleFamily.BackendGeneric, 0.7)]
    [InlineData(RoleFamily.Fullstack, 0.7)]
    [InlineData(RoleFamily.DevOpsSRE, 0.7)]
    [InlineData(RoleFamily.Frontend, 0.4)]
    [InlineData(RoleFamily.MlResearch, 0.4)]
    [InlineData(RoleFamily.DataScience, 0.4)]
    [InlineData(RoleFamily.PromptEng, 0.4)]
    [InlineData(RoleFamily.Other, 0.4)]
    [InlineData(RoleFamily.EnterpriseCrud, 0.0)]
    public void Role_family_maps_to_its_tier_score_holding_ai_usage_fixed(RoleFamily family, double tierScore)
    {
        // With High AI usage (ai score 1.0), the blend is 0.5·1.0 + 0.5·tierScore.
        var result = AlignmentCalculator.Calculate(AiUsageLevel.High, family);

        result.Value.ShouldBe((decimal)(0.5 * 1.0 + 0.5 * tierScore), tolerance: 0.0001m);
    }

    [Fact]
    public void An_unknown_ai_usage_is_treated_as_none_so_a_provider_change_degrades_safely()
    {
        // Unknown is the tolerant-parser landing place; it must not throw and must not inflate alignment.
        var unknown = AlignmentCalculator.Calculate(AiUsageLevel.Unknown, RoleFamily.Platform);
        var none = AlignmentCalculator.Calculate(AiUsageLevel.None, RoleFamily.Platform);

        unknown.Value.ShouldBe(none.Value);
    }

    [Fact]
    public void The_result_is_always_a_fraction_in_the_unit_interval()
    {
        foreach (var usage in Enum.GetValues<AiUsageLevel>())
        {
            foreach (var family in Enum.GetValues<RoleFamily>())
            {
                var value = AlignmentCalculator.Calculate(usage, family).Value;
                value.ShouldBeInRange(0m, 1m);
            }
        }
    }

    [Fact]
    public void Alignment_is_monotone_non_decreasing_in_ai_usage()
    {
        // More AI engineering content never lowers alignment (holding role family fixed).
        var none = AlignmentCalculator.Calculate(AiUsageLevel.None, RoleFamily.BackendGeneric).Value;
        var low = AlignmentCalculator.Calculate(AiUsageLevel.Low, RoleFamily.BackendGeneric).Value;
        var medium = AlignmentCalculator.Calculate(AiUsageLevel.Medium, RoleFamily.BackendGeneric).Value;
        var high = AlignmentCalculator.Calculate(AiUsageLevel.High, RoleFamily.BackendGeneric).Value;

        none.ShouldBeLessThanOrEqualTo(low);
        low.ShouldBeLessThanOrEqualTo(medium);
        medium.ShouldBeLessThanOrEqualTo(high);
    }
}
