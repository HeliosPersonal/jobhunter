using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// F7 T06: the pure function F4's ranking calls to turn the active model's weights into a bounded preference
/// component for one job, plus the per-dimension contributions the score row records (QG-1) and any conflict
/// where an explicit Profile preference overrode a contradicting learned weight (AC-05). Static and pure —
/// no clock, no repository — so its determinism is provable, like the fitter it consumes the output of. These
/// are zero-dependency unit tests; the F4 wiring and the disabled-weight-through-a-real-database path live in
/// the integration suite.
/// </summary>
public sealed class PreferenceComponentCalculatorTests
{
    private static readonly Guid ModelId = Guid.CreateVersion7();

    private static PreferenceWeight Weight(Dimension dimension, string value, decimal weight, bool disabled = false)
    {
        var w = new PreferenceWeight(
            Guid.CreateVersion7(), ModelId, dimension, value, weight,
            [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()],
            positiveRate: weight >= 0 ? 0.9m : 0.1m,
            createdAt: DateTimeOffset.UnixEpoch);
        if (disabled)
        {
            w.Disable(DateTimeOffset.UnixEpoch);
        }

        return w;
    }

    private static JobFacts Facts(Dimension dimension, string value) =>
        JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>> { [dimension] = [value] });

    [Fact]
    public void A_job_with_no_matching_weight_has_no_learned_opinion_so_the_component_is_null()
    {
        var weights = new[] { Weight(Dimension.Country, "DE", 0.4m) };

        var component = PreferenceComponentCalculator.Calculate(
            weights, Facts(Dimension.Country, "NL"), explicitStances: []);

        // Absent from the model's opinion → F4 renormalises the preference weight away rather than scoring 0.5.
        component.ShouldBeNull();
    }

    [Fact]
    public void A_single_positive_weight_maps_the_signed_pull_into_zero_to_one()
    {
        var weights = new[] { Weight(Dimension.Technology, "Kafka", 0.5m) };

        var component = PreferenceComponentCalculator.Calculate(
            weights, Facts(Dimension.Technology, "Kafka"), explicitStances: []);

        component.ShouldNotBeNull();
        component!.Value.ShouldBe(0.75m);                 // (net +0.5 + 1) / 2
        var contribution = component.Contributions.ShouldHaveSingleItem();
        contribution.Dimension.ShouldBe(Dimension.Technology);
        contribution.Value.ShouldBe("Kafka");
        contribution.Weight.ShouldBe(0.5m);
        component.Conflicts.ShouldBeEmpty();
    }

    [Fact]
    public void A_single_negative_weight_pulls_the_component_below_the_midpoint()
    {
        var weights = new[] { Weight(Dimension.Country, "DE", -0.6m) };

        var component = PreferenceComponentCalculator.Calculate(
            weights, Facts(Dimension.Country, "DE"), explicitStances: []);

        component!.Value.ShouldBe(0.2m);                  // (net -0.6 + 1) / 2
    }

    [Fact]
    public void Contributions_from_several_dimensions_sum_before_the_map()
    {
        var weights = new[]
        {
            Weight(Dimension.Technology, "Kafka", 0.3m),
            Weight(Dimension.Country, "DE", -0.1m),
        };
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Technology] = ["Kafka"],
            [Dimension.Country] = ["DE"],
        });

        var component = PreferenceComponentCalculator.Calculate(weights, facts, explicitStances: []);

        component!.Value.ShouldBe(0.6m);                  // (net +0.2 + 1) / 2
        component.Contributions.Count.ShouldBe(2);
    }

    [Fact]
    public void The_component_is_clamped_to_zero_to_one_regardless_of_the_models_contents()
    {
        // A degenerate model whose weights sum past 1 must still land in range — the clamp is unconditional.
        var weights = new[]
        {
            Weight(Dimension.Technology, "Kafka", 1.0m),
            Weight(Dimension.RemotePolicy, "remote", 1.0m),
        };
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
        {
            [Dimension.Technology] = ["Kafka"],
            [Dimension.RemotePolicy] = ["remote"],
        });

        var component = PreferenceComponentCalculator.Calculate(weights, facts, explicitStances: []);

        component!.Value.ShouldBe(1.0m);                  // (net +2 clamped to +1, + 1) / 2
    }

    [Fact]
    public void A_disabled_weight_is_excluded_immediately()
    {
        var weights = new[] { Weight(Dimension.Country, "DE", -0.6m, disabled: true) };

        var component = PreferenceComponentCalculator.Calculate(
            weights, Facts(Dimension.Country, "DE"), explicitStances: []);

        // The only applicable weight is disabled, so there is no learned opinion left (AC-06).
        component.ShouldBeNull();
    }

    [Fact]
    public void An_explicit_preference_overrides_a_contradicting_learned_weight_and_records_the_conflict()
    {
        // The Owner explicitly prefers DE; the model learned a negative DE weight from noise. Explicit wins.
        var weights = new[] { Weight(Dimension.Country, "DE", -0.6m) };
        var stances = new[] { new ExplicitStance(Dimension.Country, "DE", IsPositive: true) };

        var component = PreferenceComponentCalculator.Calculate(
            weights, Facts(Dimension.Country, "DE"), stances);

        // The contradicted weight is dropped: nothing learned remains to apply, so the component is null...
        component.ShouldNotBeNull();
        component!.Contributions.ShouldBeEmpty();
        var conflict = component.Conflicts.ShouldHaveSingleItem();
        conflict.Dimension.ShouldBe(Dimension.Country);
        conflict.Value.ShouldBe("DE");
        conflict.LearnedWeight.ShouldBe(-0.6m);
    }

    [Fact]
    public void An_explicit_preference_that_agrees_with_the_learned_weight_is_not_a_conflict()
    {
        var weights = new[] { Weight(Dimension.Country, "DE", 0.4m) };
        var stances = new[] { new ExplicitStance(Dimension.Country, "DE", IsPositive: true) };

        var component = PreferenceComponentCalculator.Calculate(
            weights, Facts(Dimension.Country, "DE"), stances);

        component!.Conflicts.ShouldBeEmpty();
        component.Contributions.ShouldHaveSingleItem().Weight.ShouldBe(0.4m);
    }
}
