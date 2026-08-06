using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Preferences;

/// <summary>
/// T01: the per-kind evidence weights (SAD §8). They are configuration with a documented default, and the
/// point of the type is that the four card actions collapse to one weight while each outcome carries its
/// own — an offer says far more than a glance.
/// </summary>
public sealed class SignalWeightsTests
{
    [Fact]
    public void The_default_weights_match_the_sad_table()
    {
        var w = SignalWeights.Default;

        w.CardAction.ShouldBe(1.0m);
        w.Applied.ShouldBe(2.0m);
        w.Rejected.ShouldBe(3.0m);
        w.Interview.ShouldBe(4.0m);
        w.Offer.ShouldBe(6.0m);
    }

    [Theory]
    [InlineData(SignalKind.Opened, 1.0)]
    [InlineData(SignalKind.Ignored, 1.0)]
    [InlineData(SignalKind.Saved, 1.0)]
    [InlineData(SignalKind.Rated, 1.0)]
    [InlineData(SignalKind.Applied, 2.0)]
    [InlineData(SignalKind.Rejected, 3.0)]
    [InlineData(SignalKind.Interview, 4.0)]
    [InlineData(SignalKind.Offer, 6.0)]
    public void Every_kind_resolves_to_its_configured_weight(SignalKind kind, double expected)
    {
        SignalWeights.Default.WeightFor(kind).ShouldBe((decimal)expected);
    }

    [Fact]
    public void All_four_card_actions_share_one_weight()
    {
        var w = new SignalWeights(cardAction: 1.5m, applied: 2m, rejected: 3m, interview: 4m, offer: 6m);

        w.WeightFor(SignalKind.Opened).ShouldBe(1.5m);
        w.WeightFor(SignalKind.Ignored).ShouldBe(1.5m);
        w.WeightFor(SignalKind.Saved).ShouldBe(1.5m);
        w.WeightFor(SignalKind.Rated).ShouldBe(1.5m);
    }

    [Fact]
    public void A_non_positive_weight_cannot_be_constructed()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new SignalWeights(cardAction: 0m, applied: 2m, rejected: 3m, interview: 4m, offer: 6m));
    }

    [Fact]
    public void Two_weight_sets_are_equal_by_their_values()
    {
        var a = new SignalWeights(1m, 2m, 3m, 4m, 6m);
        var b = new SignalWeights(1m, 2m, 3m, 4m, 6m);

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
    }
}
