using JobHunter.Domain.Intelligence;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Intelligence;

public sealed class RankingWeightsTests
{
    [Fact]
    public void The_default_weights_are_the_documented_values()
    {
        // TUNE-01/F4 T14: match 0.45, alignment 0.20, preference 0.20, freshness 0.15.
        RankingWeights.Default.Match.ShouldBe(0.45m);
        RankingWeights.Default.Alignment.ShouldBe(0.20m);
        RankingWeights.Default.Preference.ShouldBe(0.20m);
        RankingWeights.Default.Freshness.ShouldBe(0.15m);
    }

    [Fact]
    public void The_default_weights_sum_to_one()
    {
        (RankingWeights.Default.Match + RankingWeights.Default.Alignment
            + RankingWeights.Default.Preference + RankingWeights.Default.Freshness).ShouldBe(1m);
    }

    [Fact]
    public void Weights_that_do_not_sum_to_one_are_rejected()
    {
        Should.Throw<ArgumentException>(() => new RankingWeights(0.50m, 0.20m, 0.25m, 0.15m));
    }

    [Theory]
    [InlineData(-0.01, 0.31, 0.20, 0.5)]
    [InlineData(1.01, 0.0, 0.0, -0.01)]
    [InlineData(0.40, 1.01, 0.0, -0.41)]
    public void An_out_of_range_weight_is_rejected(
        decimal match, decimal alignment, decimal preference, decimal freshness)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new RankingWeights(match, alignment, preference, freshness));
    }

    [Fact]
    public void A_custom_valid_weight_set_is_accepted()
    {
        var weights = new RankingWeights(0.5m, 0.1m, 0.2m, 0.2m);

        weights.Match.ShouldBe(0.5m);
        weights.Alignment.ShouldBe(0.1m);
        weights.Preference.ShouldBe(0.2m);
        weights.Freshness.ShouldBe(0.2m);
    }

    [Fact]
    public void Equal_weight_sets_are_equal_by_value()
    {
        new RankingWeights(0.5m, 0.1m, 0.2m, 0.2m).ShouldBe(new RankingWeights(0.5m, 0.1m, 0.2m, 0.2m));
    }

    [Fact]
    public void Weight_sets_differing_only_in_alignment_are_not_equal()
    {
        new RankingWeights(0.5m, 0.1m, 0.2m, 0.2m).ShouldNotBe(new RankingWeights(0.5m, 0.2m, 0.1m, 0.2m));
    }
}
