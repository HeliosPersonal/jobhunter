using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class RankingWeightsTests
{
    [Fact]
    public void The_default_weights_are_the_documented_values()
    {
        RankingWeights.Default.Match.ShouldBe(0.60m);
        RankingWeights.Default.Preference.ShouldBe(0.25m);
        RankingWeights.Default.Freshness.ShouldBe(0.15m);
    }

    [Fact]
    public void Weights_that_do_not_sum_to_one_are_rejected()
    {
        Should.Throw<ArgumentException>(() => new RankingWeights(0.50m, 0.25m, 0.15m));
    }

    [Theory]
    [InlineData(-0.01, 0.51, 0.5)]
    [InlineData(1.01, 0.0, -0.01)]
    public void An_out_of_range_weight_is_rejected(decimal match, decimal preference, decimal freshness)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new RankingWeights(match, preference, freshness));
    }

    [Fact]
    public void A_custom_valid_weight_set_is_accepted()
    {
        var weights = new RankingWeights(0.5m, 0.3m, 0.2m);

        weights.Match.ShouldBe(0.5m);
        weights.Preference.ShouldBe(0.3m);
        weights.Freshness.ShouldBe(0.2m);
    }

    [Fact]
    public void Equal_weight_sets_are_equal_by_value()
    {
        new RankingWeights(0.5m, 0.3m, 0.2m).ShouldBe(new RankingWeights(0.5m, 0.3m, 0.2m));
    }
}
