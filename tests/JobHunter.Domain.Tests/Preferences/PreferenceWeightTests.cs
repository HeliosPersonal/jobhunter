using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Preferences;

/// <summary>
/// T01: a learned preference for one <c>(dimension, value)</c>. Its defining property (ADR-F7-0002, AC-03)
/// is that it cannot exist without at least three distinct supporting signals — the evidence floor is a
/// construction guard, not a check a caller can forget.
/// </summary>
public sealed class PreferenceWeightTests
{
    private static readonly Guid Model = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset When = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<Guid> ThreeSignals() => [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    private static PreferenceWeight NewWeight(
        decimal weight = -0.3m,
        decimal positiveRate = 0.1m,
        IReadOnlyList<Guid>? supporting = null) =>
        new(Guid.NewGuid(), Model, Dimension.Country, "DE", weight, supporting ?? ThreeSignals(), positiveRate, When);

    [Fact]
    public void A_weight_records_its_dimension_value_and_evidence()
    {
        var signals = ThreeSignals();
        var w = NewWeight(weight: -0.4m, positiveRate: 0.08m, supporting: signals);

        w.ModelId.ShouldBe(Model);
        w.Dimension.ShouldBe(Dimension.Country);
        w.Value.ShouldBe("DE");
        w.Weight.ShouldBe(-0.4m);
        w.PositiveRate.ShouldBe(0.08m);
        w.SupportingSignalIds.ShouldBe(signals);
        w.SupportingSignalCount.ShouldBe(3);
        w.Disabled.ShouldBeFalse();
        w.DisabledAt.ShouldBeNull();
    }

    [Fact]
    public void A_weight_must_belong_to_a_model()
    {
        Should.Throw<ArgumentException>(() => new PreferenceWeight(
            Guid.NewGuid(), Guid.Empty, Dimension.Country, "DE", -0.3m, ThreeSignals(), 0.1m, When));
    }

    [Fact]
    public void A_weight_must_name_a_value()
    {
        Should.Throw<ArgumentException>(() => new PreferenceWeight(
            Guid.NewGuid(), Model, Dimension.Country, "   ", -0.3m, ThreeSignals(), 0.1m, When));
    }

    [Fact]
    public void Fewer_than_three_supporting_signals_cannot_build_a_weight()
    {
        // ADR-F7-0002 / AC-03: below the floor a rate is coincidence, not preference.
        Should.Throw<ArgumentException>(() => NewWeight(supporting: [Guid.NewGuid(), Guid.NewGuid()]));
    }

    [Fact]
    public void Duplicate_supporting_signals_that_collapse_below_three_cannot_build_a_weight()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();

        // Three ids on paper, two distinct — the floor counts distinct evidence.
        Should.Throw<ArgumentException>(() => NewWeight(supporting: [one, one, two]));
    }

    [Fact]
    public void Empty_supporting_signals_are_not_counted_towards_the_floor()
    {
        Should.Throw<ArgumentException>(
            () => NewWeight(supporting: [Guid.NewGuid(), Guid.NewGuid(), Guid.Empty]));
    }

    [Fact]
    public void Supporting_signals_are_deduplicated_and_empties_dropped()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var three = Guid.NewGuid();

        var w = NewWeight(supporting: [one, one, two, three, Guid.Empty]);

        w.SupportingSignalCount.ShouldBe(3);
        w.SupportingSignalIds.ShouldBe([one, two, three]);
    }

    [Theory]
    [InlineData(-1.01)]
    [InlineData(1.01)]
    public void A_weight_outside_the_signed_unit_range_is_rejected(double weight)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewWeight(weight: (decimal)weight));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    public void The_signed_unit_bounds_are_legal(double weight)
    {
        Should.NotThrow(() => NewWeight(weight: (decimal)weight));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void A_positive_rate_outside_the_unit_interval_is_rejected(double rate)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewWeight(positiveRate: (decimal)rate));
    }

    [Fact]
    public void Disabling_records_that_it_was_switched_off_and_when()
    {
        var w = NewWeight();

        w.Disable(When);

        w.Disabled.ShouldBeTrue();
        w.DisabledAt.ShouldBe(When);
    }

    [Fact]
    public void Disabling_is_idempotent_and_keeps_the_first_timestamp()
    {
        var w = NewWeight();
        var later = When.AddDays(1);

        w.Disable(When);
        w.Disable(later);

        w.DisabledAt.ShouldBe(When);
    }
}
