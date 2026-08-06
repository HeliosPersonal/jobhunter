using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// F7 T04: the pure <see cref="WeightFitter"/>. These are the unit-level properties — recency decay, the
/// evidence floor, the indifference deadband, and the direction of a recovered weight — asserted on small,
/// hand-built signal sets. Bounding and normalisation (the 0.40 dimension cap) are asserted separately by
/// the property suite; the synthetic nine-profile corpus asserts recovery end to end. Here the assertions
/// are on <em>direction and structure</em>, which survive normalisation, not post-normalisation magnitudes.
/// </summary>
public sealed class WeightFitterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly FittingOptions Options = new(Now);

    private static SignalFact Fact(
        SignalKind kind, Dimension dimension, string value, double ageDays)
    {
        var facts = JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>> { [dimension] = [value] });
        return new SignalFact(
            Guid.CreateVersion7(),
            kind,
            SignalWeights.Default.WeightFor(kind),
            facts,
            Now.AddDays(-ageDays));
    }

    private static List<SignalFact> Repeat(int count, Func<SignalFact> make) =>
        Enumerable.Range(0, count).Select(_ => make()).ToList();

    [Fact]
    public void An_empty_history_produces_no_weights()
    {
        var model = WeightFitter.Fit([], Options);

        model.Weights.ShouldBeEmpty();
        model.SignalCount.ShouldBe(0);
    }

    [Fact]
    public void Signals_outside_the_window_are_excluded()
    {
        // Six clear-negative signals for DE, all older than the 180-day window: none feeds the fit.
        var signals = Repeat(6, () => Fact(SignalKind.Ignored, Dimension.Country, "DE", ageDays: 200));

        var model = WeightFitter.Fit(signals, Options);

        model.SignalCount.ShouldBe(0);
        model.Weights.ShouldBeEmpty();
    }

    [Fact]
    public void A_value_with_fewer_than_three_supporting_signals_earns_no_weight()
    {
        // Two saves for Kafka — a rate, but below the evidence floor, so no weight (AC-03).
        var signals = Repeat(2, () => Fact(SignalKind.Saved, Dimension.Technology, "Kafka", ageDays: 1));

        var model = WeightFitter.Fit(signals, Options);

        model.SignalCount.ShouldBe(2);
        model.Weights.ShouldBeEmpty();
    }

    [Fact]
    public void A_consistently_saved_value_earns_a_positive_weight_citing_its_signals()
    {
        var signals = Repeat(4, () => Fact(SignalKind.Saved, Dimension.Technology, "Kafka", ageDays: 3));

        var model = WeightFitter.Fit(signals, Options);

        var weight = model.Weights.ShouldHaveSingleItem();
        weight.Dimension.ShouldBe(Dimension.Technology);
        weight.Value.ShouldBe("Kafka");
        weight.Weight.ShouldBeGreaterThan(0m);
        weight.PositiveRate.ShouldBe(1m);
        weight.SupportingSignalIds.Count.ShouldBe(4);
        weight.SupportingSignalIds.ShouldBeUnique();
    }

    [Fact]
    public void A_consistently_ignored_value_earns_a_negative_weight()
    {
        var signals = Repeat(5, () => Fact(SignalKind.Ignored, Dimension.Country, "DE", ageDays: 3));

        var model = WeightFitter.Fit(signals, Options);

        var weight = model.Weights.ShouldHaveSingleItem();
        weight.Weight.ShouldBeLessThan(0m);
        weight.PositiveRate.ShouldBe(0m);
        weight.SupportingSignalIds.Count.ShouldBe(5);
    }

    [Fact]
    public void A_value_reacted_to_evenly_is_indifferent_and_earns_no_weight()
    {
        // Three saves and three ignores at the same age — a 0.5 rate, inside the deadband: no preference.
        var saves = Repeat(3, () => Fact(SignalKind.Saved, Dimension.RemotePolicy, "Hybrid", ageDays: 3));
        var ignores = Repeat(3, () => Fact(SignalKind.Ignored, Dimension.RemotePolicy, "Hybrid", ageDays: 3));

        var model = WeightFitter.Fit([.. saves, .. ignores], Options);

        model.SignalCount.ShouldBe(6);
        model.Weights.ShouldBeEmpty();
    }

    [Fact]
    public void Every_produced_weight_cites_at_least_three_signals_and_a_rate_in_the_unit_interval()
    {
        var kafka = Repeat(4, () => Fact(SignalKind.Saved, Dimension.Technology, "Kafka", ageDays: 2));
        var berlin = Repeat(5, () => Fact(SignalKind.Ignored, Dimension.Country, "DE", ageDays: 2));

        var model = WeightFitter.Fit([.. kafka, .. berlin], Options);

        model.Weights.Count.ShouldBe(2);
        foreach (var weight in model.Weights)
        {
            weight.SupportingSignalIds.Count.ShouldBeGreaterThanOrEqualTo(PreferenceWeight.MinSupportingSignals);
            weight.PositiveRate.ShouldBeInRange(0m, 1m);
            weight.Weight.ShouldBeInRange(PreferenceWeight.MinWeight, PreferenceWeight.MaxWeight);
        }
    }

    [Fact]
    public void Recent_reactions_outweigh_old_ones_of_the_opposite_sign()
    {
        // Value "remote": saved recently, ignored long ago. Flat counting would be a 0.5 tie (indifferent);
        // the 60-day half-life makes the recent saves dominate, so the recovered weight is positive.
        var recentSaves = Repeat(3, () => Fact(SignalKind.Saved, Dimension.RemotePolicy, "Remote", ageDays: 0));
        var oldIgnores = Repeat(3, () => Fact(SignalKind.Ignored, Dimension.RemotePolicy, "Remote", ageDays: 150));

        var model = WeightFitter.Fit([.. recentSaves, .. oldIgnores], Options);

        var weight = model.Weights.ShouldHaveSingleItem();
        weight.Weight.ShouldBeGreaterThan(0m);
        weight.PositiveRate.ShouldBeGreaterThan(0.5m);
        // All six reactions are the evidence for the weight, regardless of sign.
        weight.SupportingSignalIds.Count.ShouldBe(6);
    }

    [Fact]
    public void A_signal_missing_a_dimension_contributes_only_to_the_dimensions_it_carries()
    {
        // Kafka signals carry only Technology; they must not invent a Country weight.
        var signals = Repeat(4, () => Fact(SignalKind.Saved, Dimension.Technology, "Kafka", ageDays: 2));

        var model = WeightFitter.Fit(signals, Options);

        model.Weights.ShouldAllBe(w => w.Dimension == Dimension.Technology);
    }

    [Fact]
    public void The_career_trajectory_dimensions_fit_like_any_other_under_the_same_floor_and_bound()
    {
        // TUNE-08 / T10: AiUsage and RoleFamily are ordinary dimensions to the fitter. A consistently saved
        // AiPlatform role and a consistently ignored EnterpriseCrud role each earn a weight, in the right
        // direction, citing their signals and inside the [-1, +1] bound — no special-casing in the fitter.
        var aiPlatform = Repeat(4, () => Fact(SignalKind.Saved, Dimension.RoleFamily, "AiPlatform", ageDays: 2));
        var crud = Repeat(5, () => Fact(SignalKind.Ignored, Dimension.RoleFamily, "EnterpriseCrud", ageDays: 2));
        var highAi = Repeat(4, () => Fact(SignalKind.Saved, Dimension.AiUsage, "High", ageDays: 2));

        var model = WeightFitter.Fit([.. aiPlatform, .. crud, .. highAi], Options);

        var platform = model.Weights
            .Where(w => w.Dimension == Dimension.RoleFamily && w.Value == "AiPlatform")
            .ShouldHaveSingleItem();
        platform.Weight.ShouldBeGreaterThan(0m);
        platform.SupportingSignalIds.Count.ShouldBe(4);

        var enterpriseCrud = model.Weights
            .Where(w => w.Dimension == Dimension.RoleFamily && w.Value == "EnterpriseCrud")
            .ShouldHaveSingleItem();
        enterpriseCrud.Weight.ShouldBeLessThan(0m);

        var usage = model.Weights.Where(w => w.Dimension == Dimension.AiUsage).ShouldHaveSingleItem();
        usage.Value.ShouldBe("High");
        usage.Weight.ShouldBeGreaterThan(0m);
        usage.Weight.ShouldBeInRange(PreferenceWeight.MinWeight, PreferenceWeight.MaxWeight);
    }

    [Fact]
    public void An_outcome_signal_outweighs_a_card_action_of_the_opposite_sign()
    {
        // One interview (weight 4.0, positive) against three ignores (weight 1.0 each, negative) for the same
        // value: the consequential outcome tips the recency-flat rate positive despite being outnumbered. The
        // fitter reads only kind/weight/facts, so the fact need not carry an application id.
        var interview = Fact(SignalKind.Interview, Dimension.Country, "NL", ageDays: 2);
        var ignores = Repeat(3, () => Fact(SignalKind.Ignored, Dimension.Country, "NL", ageDays: 2));

        var model = WeightFitter.Fit([interview, .. ignores], Options);

        var weight = model.Weights.ShouldHaveSingleItem();
        weight.Weight.ShouldBeGreaterThan(0m);
        weight.PositiveRate.ShouldBeGreaterThan(0.5m);
    }
}
