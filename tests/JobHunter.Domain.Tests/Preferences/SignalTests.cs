using JobHunter.Domain.Preferences;
using Shouldly;
using Xunit;

namespace JobHunter.Domain.Tests.Preferences;

/// <summary>
/// T01: one recorded reaction. The guards are the type's job — a signal must reference a job, must carry a
/// non-empty facts snapshot, must weigh something, and must agree with itself about whether it is a card
/// action or an application outcome.
/// </summary>
public sealed class SignalTests
{
    private static readonly Guid Job = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Application = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset When = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private static JobFacts Facts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.Country] = ["DE"],
    });

    [Fact]
    public void A_card_action_signal_records_its_kind_facts_and_weight()
    {
        var signal = new Signal(Guid.NewGuid(), Job, applicationId: null, SignalKind.Saved, 1.0m, Facts(), When);

        signal.JobId.ShouldBe(Job);
        signal.ApplicationId.ShouldBeNull();
        signal.Kind.ShouldBe(SignalKind.Saved);
        signal.Weight.ShouldBe(1.0m);
        signal.JobFacts.ShouldBe(Facts());
        signal.OccurredAt.ShouldBe(When);
    }

    [Fact]
    public void A_signal_must_reference_a_job()
    {
        Should.Throw<ArgumentException>(
            () => new Signal(Guid.NewGuid(), Guid.Empty, null, SignalKind.Opened, 1m, Facts(), When));
    }

    [Fact]
    public void A_signal_must_carry_a_facts_snapshot()
    {
        Should.Throw<ArgumentNullException>(
            () => new Signal(Guid.NewGuid(), Job, null, SignalKind.Opened, 1m, jobFacts: null!, When));
    }

    [Fact]
    public void A_signal_weight_must_be_strictly_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new Signal(Guid.NewGuid(), Job, null, SignalKind.Opened, 0m, Facts(), When));
    }

    [Theory]
    [InlineData(SignalKind.Applied)]
    [InlineData(SignalKind.Interview)]
    [InlineData(SignalKind.Offer)]
    [InlineData(SignalKind.Rejected)]
    public void An_outcome_signal_must_reference_its_application(SignalKind kind)
    {
        Should.Throw<ArgumentException>(
            () => new Signal(Guid.NewGuid(), Job, applicationId: null, kind, 2m, Facts(), When));
    }

    [Theory]
    [InlineData(SignalKind.Applied)]
    [InlineData(SignalKind.Interview)]
    [InlineData(SignalKind.Offer)]
    [InlineData(SignalKind.Rejected)]
    public void An_outcome_signal_rejects_an_empty_application_id(SignalKind kind)
    {
        Should.Throw<ArgumentException>(
            () => new Signal(Guid.NewGuid(), Job, Guid.Empty, kind, 2m, Facts(), When));
    }

    [Theory]
    [InlineData(SignalKind.Opened)]
    [InlineData(SignalKind.Ignored)]
    [InlineData(SignalKind.Saved)]
    [InlineData(SignalKind.Rated)]
    public void A_card_action_signal_must_not_reference_an_application(SignalKind kind)
    {
        Should.Throw<ArgumentException>(
            () => new Signal(Guid.NewGuid(), Job, Application, kind, 1m, Facts(), When));
    }

    [Fact]
    public void An_outcome_signal_keeps_its_application_reference()
    {
        var signal = new Signal(Guid.NewGuid(), Job, Application, SignalKind.Offer, 6m, Facts(), When);

        signal.ApplicationId.ShouldBe(Application);
    }

    [Fact]
    public void Capture_resolves_the_card_action_weight_from_the_configured_table()
    {
        var signal = Signal.Capture(
            Guid.NewGuid(), Job, applicationId: null, SignalKind.Ignored, Facts(), When, SignalWeights.Default);

        signal.Weight.ShouldBe(1.0m);
    }

    [Fact]
    public void Capture_resolves_the_outcome_weight_from_the_configured_table()
    {
        var signal = Signal.Capture(
            Guid.NewGuid(), Job, Application, SignalKind.Interview, Facts(), When, SignalWeights.Default);

        signal.Weight.ShouldBe(4.0m);
    }

    [Fact]
    public void Capture_requires_a_weight_table()
    {
        Should.Throw<ArgumentNullException>(() => Signal.Capture(
            Guid.NewGuid(), Job, null, SignalKind.Opened, Facts(), When, weights: null!));
    }
}
