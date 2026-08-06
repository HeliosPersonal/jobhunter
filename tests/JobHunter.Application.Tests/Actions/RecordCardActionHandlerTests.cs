using JobHunter.Application.Actions;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Preferences;
using JobHunter.TestKit;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Actions;

/// <summary>
/// T10 (app half): the command that turns a card tap into durable evidence (AC-03, AC-08). The load-bearing
/// properties: an <c>Ignore</c> or a <c>Save</c> captures exactly one card-action <see cref="Signal"/> — the
/// right <see cref="SignalKind"/>, weight 1.0, no application, the tap's own instant, and a
/// <see cref="JobFacts"/> snapshot read <em>at the tap</em> so a later job edit cannot rewrite it (AC-08). A
/// second identical tap captures nothing more (idempotent). A tap on a job that no longer exists or has
/// closed records nothing invalid and says so (AC-09). <c>Applied</c> is an F6 outcome kind that needs an
/// application id F5 does not have, so F5 writes no signal for it — its durable record is
/// <c>OwnerActionRecorded</c> → F6; the handler reports that plainly rather than inventing a card-action
/// signal. Every collaborator is faked: zero database, zero network.
/// </summary>
public sealed class RecordCardActionHandlerTests
{
    private static readonly Guid JobId = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private static readonly DateTimeOffset TapAt = new(2026, 8, 6, 5, 30, 0, TimeSpan.Zero);

    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly ISignalRepository _signals = Substitute.For<ISignalRepository>();
    private readonly SequentialIdGenerator _ids = new();

    private static JobFacts SampleFacts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.Country] = ["DE"],
        [Dimension.Technology] = ["Kafka", "Azure"],
    });

    private RecordCardActionHandler Build() => new(_facts, _signals, _ids);

    private RecordCardActionHandler BuildWithFacts(JobFacts? snapshot)
    {
        _facts.SnapshotAsync(JobId, Arg.Any<CancellationToken>()).Returns(snapshot);
        return Build();
    }

    [Theory]
    [InlineData(CardAction.Ignore, SignalKind.Ignored)]
    [InlineData(CardAction.Save, SignalKind.Saved)]
    public async Task A_card_action_captures_one_card_signal_with_the_snapshot_at_the_tap(
        CardAction action, SignalKind expectedKind)
    {
        Signal? captured = null;
        _signals.TryCaptureAsync(Arg.Do<Signal>(s => captured = s), Arg.Any<CancellationToken>()).Returns(true);
        var handler = BuildWithFacts(SampleFacts());

        var outcome = await handler.Handle(new RecordCardActionCommand(JobId, action, TapAt), CancellationToken.None);

        outcome.ShouldBe(CardActionOutcome.Captured);
        captured.ShouldNotBeNull();
        captured!.JobId.ShouldBe(JobId);
        captured.Kind.ShouldBe(expectedKind);
        captured.ApplicationId.ShouldBeNull();
        captured.Weight.ShouldBe(1.0m);
        captured.OccurredAt.ShouldBe(TapAt);
        captured.JobFacts.ShouldBe(SampleFacts());
    }

    [Fact]
    public async Task A_repeated_tap_captures_nothing_more_and_reports_it()
    {
        _signals.TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = BuildWithFacts(SampleFacts());

        var outcome = await handler.Handle(new RecordCardActionCommand(JobId, CardAction.Save, TapAt), CancellationToken.None);

        outcome.ShouldBe(CardActionOutcome.AlreadyCaptured);
    }

    [Fact]
    public async Task A_tap_on_a_missing_or_closed_job_records_nothing_and_reports_it()
    {
        var handler = BuildWithFacts(null);

        var outcome = await handler.Handle(new RecordCardActionCommand(JobId, CardAction.Ignore, TapAt), CancellationToken.None);

        outcome.ShouldBe(CardActionOutcome.JobUnavailable);
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Applied_writes_no_F5_signal_because_it_is_an_F6_outcome()
    {
        var handler = Build();

        var outcome = await handler.Handle(new RecordCardActionCommand(JobId, CardAction.Applied, TapAt), CancellationToken.None);

        outcome.ShouldBe(CardActionOutcome.RecordedElsewhere);
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
        await _facts.DidNotReceive().SnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Open_writes_no_signal_because_it_is_a_url_button()
    {
        var handler = Build();

        var outcome = await handler.Handle(new RecordCardActionCommand(JobId, CardAction.Open, TapAt), CancellationToken.None);

        outcome.ShouldBe(CardActionOutcome.RecordedElsewhere);
        await _signals.DidNotReceive().TryCaptureAsync(Arg.Any<Signal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_null_command_is_rejected()
    {
        var handler = Build();

        await Should.ThrowAsync<ArgumentNullException>(() => handler.Handle(null!, CancellationToken.None));
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new RecordCardActionHandler(null!, _signals, _ids));
        Should.Throw<ArgumentNullException>(() => new RecordCardActionHandler(_facts, null!, _ids));
        Should.Throw<ArgumentNullException>(() => new RecordCardActionHandler(_facts, _signals, null!));
    }
}
