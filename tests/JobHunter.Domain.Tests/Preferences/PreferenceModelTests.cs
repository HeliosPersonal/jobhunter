using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Preferences;

/// <summary>
/// T01: one fitted version of the Owner's preferences. It is immutable, activation is a separate operation,
/// and a model fitted on fewer than <see cref="PreferenceModel.ActivationThreshold"/> signals cannot be
/// switched on — the two-week evidence floor (ADR-F7-0002) guards the flip.
/// </summary>
public sealed class PreferenceModelTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private static PreferenceWeight WeightFor(Guid modelId) =>
        new(Guid.NewGuid(), modelId, Dimension.Country, "DE", -0.3m,
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], 0.1m, When);

    private static PreferenceModel NewModel(
        Guid? id = null,
        int version = 1,
        int signalCount = 200,
        IReadOnlyList<PreferenceWeight>? weights = null,
        string? notes = null)
    {
        var modelId = id ?? Guid.NewGuid();
        return new PreferenceModel(
            modelId, version, signalCount, weights ?? [WeightFor(modelId)], When, notes);
    }

    [Fact]
    public void A_model_bundles_its_version_evidence_and_weights()
    {
        var id = Guid.NewGuid();
        var weight = WeightFor(id);

        var model = new PreferenceModel(id, version: 3, signalCount: 250, [weight], When, notes: null);

        model.Version.ShouldBe(3);
        model.SignalCount.ShouldBe(250);
        model.Weights.ShouldBe([weight]);
        model.FittedAt.ShouldBe(When);
        model.IsActive.ShouldBeFalse();
        model.ActivatedAt.ShouldBeNull();
    }

    [Fact]
    public void A_fresh_model_starts_inactive()
    {
        NewModel().IsActive.ShouldBeFalse();
    }

    [Fact]
    public void A_version_must_be_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewModel(version: 0));
    }

    [Fact]
    public void A_signal_count_cannot_be_negative()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewModel(signalCount: -1));
    }

    [Fact]
    public void Every_weight_must_reference_this_model()
    {
        var foreignWeight = WeightFor(Guid.NewGuid());

        Should.Throw<ArgumentException>(() => new PreferenceModel(
            Guid.NewGuid(), 1, 200, [foreignWeight], When));
    }

    [Fact]
    public void An_indifferent_owner_earns_a_model_with_no_weights()
    {
        // The F7 "indifferent profile that must produce no weights" floor — an empty weight set is legal.
        var model = NewModel(weights: []);

        model.Weights.ShouldBeEmpty();
    }

    [Fact]
    public void Activation_is_a_separate_operation_from_construction()
    {
        var model = NewModel(signalCount: 200);

        model.IsActive.ShouldBeFalse();
        model.Activate(When);
        model.IsActive.ShouldBeTrue();
        model.ActivatedAt.ShouldBe(When);
    }

    [Fact]
    public void A_model_below_the_evidence_floor_cannot_be_activated()
    {
        var model = NewModel(signalCount: PreferenceModel.ActivationThreshold - 1);

        Should.Throw<InvalidOperationException>(() => model.Activate(When));
        model.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void A_model_exactly_at_the_evidence_floor_can_be_activated()
    {
        var model = NewModel(signalCount: PreferenceModel.ActivationThreshold);

        Should.NotThrow(() => model.Activate(When));
        model.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Activation_is_idempotent()
    {
        var model = NewModel(signalCount: 200);
        var later = When.AddDays(1);

        model.Activate(When);
        model.Activate(later);

        model.ActivatedAt.ShouldBe(When);
    }

    [Fact]
    public void Deactivation_turns_a_model_off_and_clears_its_activation()
    {
        var model = NewModel(signalCount: 200);
        model.Activate(When);

        model.Deactivate();

        model.IsActive.ShouldBeFalse();
        model.ActivatedAt.ShouldBeNull();
    }

    [Fact]
    public void Deactivation_is_idempotent_on_an_inactive_model()
    {
        var model = NewModel(signalCount: 200);

        Should.NotThrow(model.Deactivate);
        model.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Insufficient_evidence_can_be_recorded_in_the_notes()
    {
        var model = NewModel(signalCount: 143, notes: "insufficient evidence: 143 signals");

        model.HasSufficientEvidence.ShouldBeFalse();
        model.Notes.ShouldBe("insufficient evidence: 143 signals");
    }

    [Fact]
    public void Blank_notes_read_back_as_null()
    {
        NewModel(notes: "   ").Notes.ShouldBeNull();
    }
}
