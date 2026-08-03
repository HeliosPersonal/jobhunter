using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class ScoreComponentsTests
{
    [Fact]
    public void Valid_components_expose_their_values()
    {
        var components = new ScoreComponents(0.80m, 0.50m, 0.40m, 0.85m);

        components.Match.ShouldBe(0.80m);
        components.Preference.ShouldBe(0.50m);
        components.Freshness.ShouldBe(0.40m);
        components.ConfidenceMultiplier.ShouldBe(0.85m);
    }

    [Theory]
    [InlineData(-0.01, 0.5, 0.5)]
    [InlineData(1.01, 0.5, 0.5)]
    [InlineData(0.5, -0.01, 0.5)]
    [InlineData(0.5, 1.01, 0.5)]
    [InlineData(0.5, 0.5, -0.01)]
    [InlineData(0.5, 0.5, 1.01)]
    public void An_out_of_range_component_is_rejected(decimal match, decimal preference, decimal freshness)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ScoreComponents(match, preference, freshness, 1.00m));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.01)]
    public void An_out_of_range_confidence_multiplier_is_rejected(decimal confidence)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ScoreComponents(0.5m, 0.5m, 0.5m, confidence));
    }

    [Fact]
    public void Reconcile_applies_the_weights_and_confidence()
    {
        var components = new ScoreComponents(1.0m, 1.0m, 1.0m, 1.0m);

        // All components at 1 and weights summing to 1 => weighted term = 1 => 100 × 1 × 1.
        components.Reconcile(RankingWeights.Default).ShouldBe(100m);
    }

    [Fact]
    public void Reconcile_uses_the_default_weights_as_documented()
    {
        var components = new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);

        // 100 × (0.60·0.80 + 0.25·0.50 + 0.15·0.40) × 1.00 = 100 × (0.48 + 0.125 + 0.06) = 66.5
        components.Reconcile(RankingWeights.Default).ShouldBe(66.5m);
    }

    [Fact]
    public void Confidence_scales_the_whole_score()
    {
        var full = new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);
        var degraded = new ScoreComponents(0.80m, 0.50m, 0.40m, 0.85m);

        degraded.Reconcile(RankingWeights.Default)
            .ShouldBe(full.Reconcile(RankingWeights.Default) * 0.85m);
    }

    [Fact]
    public void Equal_components_are_equal_by_value()
    {
        var a = new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);
        var b = new ScoreComponents(0.80m, 0.50m, 0.40m, 1.00m);

        a.ShouldBe(b);
    }
}
