using JobHunter.Application.Applications;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// T08 done-when 4: the outcome signal weights are configuration, documented in SAD §8, not hard-coded.
/// <see cref="SignalWeightOptions"/> binds the per-kind weights and builds the <see cref="SignalWeights"/> the
/// publisher resolves each signal's weight through. Its defaults are the SAD §8 table, so an unconfigured
/// deployment behaves exactly as <see cref="SignalWeights.Default"/>.
/// </summary>
public sealed class SignalWeightOptionsTests
{
    [Fact]
    public void The_defaults_are_the_sad_weights()
    {
        new SignalWeightOptions().ToWeights().ShouldBe(SignalWeights.Default);
    }

    [Fact]
    public void Configured_weights_override_the_defaults()
    {
        var options = new SignalWeightOptions
        {
            CardAction = 1.5m,
            Applied = 2.5m,
            Rejected = 3.5m,
            Interview = 4.5m,
            Offer = 6.5m,
        };

        var weights = options.ToWeights();

        weights.WeightFor(SignalKind.Saved).ShouldBe(1.5m);
        weights.WeightFor(SignalKind.Applied).ShouldBe(2.5m);
        weights.WeightFor(SignalKind.Rejected).ShouldBe(3.5m);
        weights.WeightFor(SignalKind.Interview).ShouldBe(4.5m);
        weights.WeightFor(SignalKind.Offer).ShouldBe(6.5m);
    }
}
