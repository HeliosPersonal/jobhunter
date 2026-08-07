using JobHunter.Application.Preferences;
using JobHunter.Domain.Preferences;
using JobHunter.TestKit;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Preferences;

/// <summary>
/// T08 (AC-06, second half): the refit must not silently relearn a preference the Owner switched off. A
/// disabled weight is carried into the new model version — still disabled — until the fresh fit shows the
/// supporting evidence has <em>doubled</em>, at which point it is allowed to relearn as a fresh, enabled
/// weight. The doubling baseline is the disabled weight's own <see cref="PreferenceWeight.SupportingSignalCount"/>:
/// weights are immutable, so that count is frozen at the instant the Owner disabled it, needing no extra column.
/// This is the pure planner the learner runs while building the new version's weights.
/// </summary>
public sealed class DisabledWeightCarryForwardTests
{
    private static readonly DateTimeOffset FittedAt = new(2026, 8, 7, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid PriorModelId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid NewModelId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly SequentialIdGenerator _ids = new();

    private static IReadOnlyList<Guid> Signals(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => Guid.NewGuid())];

    private static FittedWeight Fitted(Dimension dimension, string value, decimal weight, int supporting) =>
        new(dimension, value, weight, weight >= 0 ? 0.8m : 0.2m, Signals(supporting));

    private static PreferenceModel PriorWith(params PreferenceWeight[] weights)
    {
        var model = new PreferenceModel(PriorModelId, version: 1, signalCount: 250, weights, FittedAt.AddDays(-7));
        model.Activate(FittedAt.AddDays(-7));
        return model;
    }

    private PreferenceWeight PriorWeight(
        Dimension dimension, string value, int supporting, bool disabled, DateTimeOffset? disabledAt = null)
    {
        var w = new PreferenceWeight(
            _ids.NewId(), PriorModelId, dimension, value, -0.6m, Signals(supporting), 0.2m, FittedAt.AddDays(-7));
        if (disabled)
        {
            w.Disable(disabledAt ?? FittedAt.AddDays(-3));
        }

        return w;
    }

    private IReadOnlyList<PreferenceWeight> Apply(
        PreferenceModel? prior, IReadOnlyList<FittedWeight> fitted) =>
        DisabledWeightCarryForward.Apply(prior, NewModelId, fitted, _ids.NewId, FittedAt);

    [Fact]
    public void With_no_prior_model_every_fitted_weight_is_emitted_fresh_and_enabled()
    {
        var fitted = new[] { Fitted(Dimension.Country, "DE", -0.6m, 10), Fitted(Dimension.Technology, "Kafka", 0.5m, 8) };

        var result = Apply(prior: null, fitted);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(w => !w.Disabled && w.ModelId == NewModelId);
    }

    [Fact]
    public void A_disabled_weight_whose_evidence_has_not_doubled_is_carried_forward_still_disabled()
    {
        var prior = PriorWith(PriorWeight(Dimension.Country, "DE", supporting: 4, disabled: true));
        // Fresh fit sees 7 supporting signals for DE — more, but not yet 8 (double of 4).
        var fitted = new[] { Fitted(Dimension.Country, "DE", -0.6m, supporting: 7) };

        var result = Apply(prior, fitted);

        var de = result.ShouldHaveSingleItem();
        de.Dimension.ShouldBe(Dimension.Country);
        de.Value.ShouldBe("DE");
        de.Disabled.ShouldBeTrue();               // the Owner's choice survives the refit
        de.ModelId.ShouldBe(NewModelId);          // re-created under the new version
        de.SupportingSignalCount.ShouldBe(4);     // baseline preserved, so doubling is measured from the same floor
    }

    [Fact]
    public void A_disabled_weight_whose_evidence_has_doubled_is_relearned_fresh_and_enabled()
    {
        var prior = PriorWith(PriorWeight(Dimension.Country, "DE", supporting: 4, disabled: true));
        // Fresh fit sees 8 supporting signals — exactly double of 4: the boundary belongs to relearning.
        var fitted = new[] { Fitted(Dimension.Country, "DE", -0.6m, supporting: 8) };

        var result = Apply(prior, fitted);

        var de = result.ShouldHaveSingleItem();
        de.Disabled.ShouldBeFalse();              // enough new evidence — the preference is learned again
        de.SupportingSignalCount.ShouldBe(8);     // the fresh evidence, not the old baseline
    }

    [Fact]
    public void A_disabled_value_that_does_not_reappear_in_the_fresh_fit_is_still_carried_forward_disabled()
    {
        var prior = PriorWith(PriorWeight(Dimension.Country, "DE", supporting: 4, disabled: true));
        // The fresh fit produced nothing for DE (fell below the evidence floor or into the indifference band):
        // zero new evidence certainly has not doubled, so the disable must persist rather than silently vanish.
        var fitted = new[] { Fitted(Dimension.Technology, "Kafka", 0.5m, supporting: 9) };

        var result = Apply(prior, fitted);

        result.Count.ShouldBe(2);
        result.ShouldContain(w => w.Dimension == Dimension.Country && w.Value == "DE" && w.Disabled);
        result.ShouldContain(w => w.Dimension == Dimension.Technology && w.Value == "Kafka" && !w.Disabled);
    }

    [Fact]
    public void A_carried_disabled_weight_preserves_its_switch_off_instant()
    {
        var switchedOffAt = FittedAt.AddDays(-2);
        var prior = PriorWith(PriorWeight(Dimension.Country, "DE", supporting: 4, disabled: true, disabledAt: switchedOffAt));
        var fitted = new[] { Fitted(Dimension.Country, "DE", -0.6m, supporting: 5) };

        var result = Apply(prior, fitted);

        result.ShouldHaveSingleItem().DisabledAt.ShouldBe(switchedOffAt);
    }

    [Fact]
    public void A_prior_enabled_weight_is_not_carried_it_simply_refits_fresh()
    {
        // Only the Owner's explicit disables are preserved; an enabled prior weight is ordinary evidence that
        // the fresh fit re-derives (or drops) on its own — carrying it would freeze the model against learning.
        var prior = PriorWith(PriorWeight(Dimension.Country, "DE", supporting: 4, disabled: false));
        var fitted = new[] { Fitted(Dimension.Technology, "Kafka", 0.5m, supporting: 9) };

        var result = Apply(prior, fitted);

        result.ShouldHaveSingleItem().Dimension.ShouldBe(Dimension.Technology);
        result.ShouldNotContain(w => w.Dimension == Dimension.Country);
    }
}
