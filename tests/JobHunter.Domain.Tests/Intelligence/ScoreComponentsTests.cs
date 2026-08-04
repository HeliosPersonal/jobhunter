using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class ScoreComponentsTests
{
    [Fact]
    public void Valid_components_expose_their_values()
    {
        var components = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 0.85m);

        components.Match.ShouldBe(0.80m);
        components.Alignment.ShouldBe(0.60m);
        components.Preference.ShouldBe(0.50m);
        components.Freshness.ShouldBe(0.40m);
        components.ConfidenceMultiplier.ShouldBe(0.85m);
        // The anti-goal multiplier defaults to 1.00 — no penalty — so an ordinary role is untouched (T15).
        components.AntiGoalMultiplier.ShouldBe(1.00m);
    }

    [Fact]
    public void The_anti_goal_multiplier_is_stored_when_supplied()
    {
        var components = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m, antiGoalMultiplier: 0.50m);

        components.AntiGoalMultiplier.ShouldBe(0.50m);
    }

    [Theory]
    [InlineData(-0.01, 0.5, 0.5, 0.5)]
    [InlineData(1.01, 0.5, 0.5, 0.5)]
    [InlineData(0.5, -0.01, 0.5, 0.5)]
    [InlineData(0.5, 1.01, 0.5, 0.5)]
    [InlineData(0.5, 0.5, -0.01, 0.5)]
    [InlineData(0.5, 0.5, 1.01, 0.5)]
    [InlineData(0.5, 0.5, 0.5, -0.01)]
    [InlineData(0.5, 0.5, 0.5, 1.01)]
    public void An_out_of_range_component_is_rejected(
        decimal match, decimal alignment, decimal preference, decimal freshness)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ScoreComponents(match, alignment, preference, freshness, 1.00m));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.01)]
    public void An_out_of_range_confidence_multiplier_is_rejected(decimal confidence)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ScoreComponents(0.5m, 0.5m, 0.5m, 0.5m, confidence));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void An_out_of_range_anti_goal_multiplier_is_rejected(decimal antiGoal)
    {
        // The anti-goal factor down-weights, so zero is legal (a full penalty) but negative or above 1 is not.
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ScoreComponents(0.5m, 0.5m, 0.5m, 0.5m, 1.00m, antiGoalMultiplier: antiGoal));
    }

    [Fact]
    public void Reconcile_applies_the_weights_and_confidence()
    {
        var components = new ScoreComponents(1.0m, 1.0m, 1.0m, 1.0m, 1.0m);

        // All components at 1 and weights summing to 1 => weighted term = 1 => 100 × 1 × 1.
        components.Reconcile(RankingWeights.Default).ShouldBe(100m);
    }

    [Fact]
    public void Reconcile_uses_the_default_weights_as_documented()
    {
        var components = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m);

        // 100 × (0.45·0.80 + 0.20·0.60 + 0.20·0.50 + 0.15·0.40) × 1.00
        //   = 100 × (0.36 + 0.12 + 0.10 + 0.06) = 64.0
        components.Reconcile(RankingWeights.Default).ShouldBe(64.0m);
    }

    [Fact]
    public void Confidence_scales_the_whole_score()
    {
        var full = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m);
        var degraded = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 0.85m);

        degraded.Reconcile(RankingWeights.Default)
            .ShouldBe(full.Reconcile(RankingWeights.Default) * 0.85m);
    }

    [Fact]
    public void The_anti_goal_multiplier_scales_the_whole_score_like_confidence()
    {
        var full = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m);
        var penalised = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m, antiGoalMultiplier: 0.50m);

        // Multiplicative, like confidence: an anti-goal role keeps its components but halves its total (T15).
        penalised.Reconcile(RankingWeights.Default)
            .ShouldBe(full.Reconcile(RankingWeights.Default) * 0.50m);
    }

    [Fact]
    public void Equal_components_are_equal_by_value()
    {
        var a = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m);
        var b = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m);

        a.ShouldBe(b);
    }

    [Fact]
    public void Components_differing_only_in_alignment_are_not_equal()
    {
        var a = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m);
        var b = new ScoreComponents(0.80m, 0.30m, 0.50m, 0.40m, 1.00m);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Components_differing_only_in_the_anti_goal_multiplier_are_not_equal()
    {
        var a = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m);
        var b = new ScoreComponents(0.80m, 0.60m, 0.50m, 0.40m, 1.00m, antiGoalMultiplier: 0.50m);

        a.ShouldNotBe(b);
    }
}
