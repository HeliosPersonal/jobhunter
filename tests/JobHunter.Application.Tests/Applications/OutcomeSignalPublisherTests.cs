using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Preferences;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// T08: reaching a terminal outcome (<see cref="ApplicationStatus.Applied"/>, <see cref="ApplicationStatus.Interview"/>,
/// <see cref="ApplicationStatus.Offer"/>, <see cref="ApplicationStatus.Rejected"/>) stages one weighted
/// <see cref="Signal"/> for F7 (AC-08). The load-bearing properties: the signal carries the SAD §8 weight for
/// its kind (from injected configuration, not a literal), references the application it came from, and captures
/// the job's <see cref="JobFacts"/> <em>as read at that moment</em> so a later job edit cannot rewrite history.
/// A non-outcome status stages nothing (a <c>Saved</c>/<c>Ignored</c> move is F5's card-action signal, not an
/// outcome; <c>New</c> is not an outcome at all), so F6 never double-counts F5's evidence. The publisher only
/// stages into the caller's unit of work — it never commits — so the signal is written in the same transaction
/// as the transition. Every collaborator is faked: zero database, zero network.
/// </summary>
public sealed class OutcomeSignalPublisherTests
{
    private static readonly Guid Job = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Application = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset At = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    // Deliberately not the SAD defaults, to prove the weight comes from injected configuration, not a literal.
    private static readonly SignalWeights Weights = new(cardAction: 1.5m, applied: 2.5m, rejected: 3.5m, interview: 4.5m, offer: 6.5m);

    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly FakeOutcomeSignalWriter _signals = new();
    private readonly SequentialIdGenerator _ids = new();

    private static JobFacts SampleFacts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.Country] = ["DE"],
        [Dimension.Technology] = ["Kafka", "Azure"],
    });

    private OutcomeSignalPublisher Build(JobFacts? snapshot)
    {
        _facts.SnapshotAsync(Job, Arg.Any<CancellationToken>()).Returns(snapshot);
        return new OutcomeSignalPublisher(_facts, _signals, _ids, Weights, NullLogger<OutcomeSignalPublisher>.Instance);
    }

    private Task Stage(ApplicationStatus to, JobFacts? snapshot) =>
        Build(snapshot).StageAsync(Job, Application, to, At, CancellationToken.None);

    [Theory]
    [InlineData(ApplicationStatus.Applied, SignalKind.Applied, 2.5)]
    [InlineData(ApplicationStatus.Interview, SignalKind.Interview, 4.5)]
    [InlineData(ApplicationStatus.Offer, SignalKind.Offer, 6.5)]
    [InlineData(ApplicationStatus.Rejected, SignalKind.Rejected, 3.5)]
    public async Task Reaching_an_outcome_stages_one_weighted_signal_with_the_snapshot_at_that_moment(
        ApplicationStatus to, SignalKind expectedKind, double expectedWeight)
    {
        await Stage(to, SampleFacts());

        var signal = _signals.Staged.ShouldHaveSingleItem();
        signal.JobId.ShouldBe(Job);
        signal.ApplicationId.ShouldBe(Application);
        signal.Kind.ShouldBe(expectedKind);
        signal.Weight.ShouldBe((decimal)expectedWeight);
        signal.OccurredAt.ShouldBe(At);
        signal.JobFacts.ShouldBe(SampleFacts());
    }

    [Theory]
    [InlineData(ApplicationStatus.New)]
    [InlineData(ApplicationStatus.Saved)]
    [InlineData(ApplicationStatus.Ignored)]
    public async Task A_non_outcome_status_stages_no_signal(ApplicationStatus to)
    {
        await Stage(to, SampleFacts());

        // Saved/Ignored are F5 card-action signals, New is not an outcome — F6 never mints one here, so the
        // snapshot is not even read: there is nothing to attach it to.
        _signals.Staged.ShouldBeEmpty();
        await _facts.DidNotReceive().SnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_outcome_on_a_job_that_has_closed_stages_no_signal()
    {
        // The facts snapshot returns only Live jobs; a closed job is null. A signal needs non-empty facts, so
        // rather than fabricate a factless one, stage nothing — the transition still stands, F7 loses one point.
        await Stage(ApplicationStatus.Applied, snapshot: null);

        _signals.Staged.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_repeated_outcome_at_the_same_moment_stages_no_second_signal_within_the_call()
    {
        // The publisher is idempotent per unit of work by not staging a second signal it can already see staged
        // for the same (job, kind, moment) — the belt to the database unique constraint's braces.
        var publisher = Build(SampleFacts());

        await publisher.StageAsync(Job, Application, ApplicationStatus.Applied, At, CancellationToken.None);
        await publisher.StageAsync(Job, Application, ApplicationStatus.Applied, At, CancellationToken.None);

        _signals.Staged.Count.ShouldBe(1);
    }

    [Fact]
    public void Null_dependencies_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new OutcomeSignalPublisher(null!, _signals, _ids, Weights, NullLogger<OutcomeSignalPublisher>.Instance));
        Should.Throw<ArgumentNullException>(() => new OutcomeSignalPublisher(_facts, null!, _ids, Weights, NullLogger<OutcomeSignalPublisher>.Instance));
        Should.Throw<ArgumentNullException>(() => new OutcomeSignalPublisher(_facts, _signals, null!, Weights, NullLogger<OutcomeSignalPublisher>.Instance));
        Should.Throw<ArgumentNullException>(() => new OutcomeSignalPublisher(_facts, _signals, _ids, null!, NullLogger<OutcomeSignalPublisher>.Instance));
        Should.Throw<ArgumentNullException>(() => new OutcomeSignalPublisher(_facts, _signals, _ids, Weights, null!));
    }

    /// <summary>
    /// A stateful <see cref="IOutcomeSignalWriter"/> double: it records what was staged and can report whether
    /// an identical signal is already present, so the publisher's within-call idempotency is exercised without a
    /// database. The real EF writer (which shares the caller's <c>DbContext</c>, so the signal commits in the
    /// same transaction as the transition) is proven in the Infrastructure integration suite.
    /// </summary>
    private sealed class FakeOutcomeSignalWriter : IOutcomeSignalWriter
    {
        public List<Signal> Staged { get; } = [];

        public bool IsStaged(Guid jobId, SignalKind kind, DateTimeOffset occurredAt) =>
            Staged.Any(s => s.JobId == jobId && s.Kind == kind && s.OccurredAt == occurredAt);

        public void Stage(Signal signal) => Staged.Add(signal);
    }
}
