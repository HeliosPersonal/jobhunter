using JobHunter.Application.Applications;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Preferences;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// T03: the owner-action handler turns a digest action (<see cref="OwnerActionRecorded"/>, F5 T10) into a
/// tracked application. It creates the application lazily on the first action (SAD §4 S2), evaluates the
/// transition against <see cref="TransitionRules"/>, applies it as append-only history (QG-1), and publishes
/// <see cref="ApplicationStatusChanged"/> so F7 and F9 react — all in one Wolverine EF transaction (AC-03).
///
/// <para>Idempotence is the SAD §8 key <c>(application_id, to_status, occurred_at)</c>: a redelivered
/// identical action finds its transition already present and appends no second one. The repository is a
/// small stateful fake so the two-call idempotency and the lazy-create paths are exercised without a
/// database — the real-database proof of AC-04 lives in the Infrastructure integration suite.</para>
/// </summary>
public sealed class OwnerActionHandlerTests
{
    private static readonly Guid Job = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset T0 = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);
    private const long ChatId = 4242;

    private readonly FakeApplicationRepository _repo = new();
    private readonly SequentialIdGenerator _ids = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly FakeOutcomeSignalWriter _signals = new();

    private static JobFacts SampleFacts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.Country] = ["DE"],
    });

    private OwnerActionHandler CreateHandler()
    {
        _facts.SnapshotAsync(Job, Arg.Any<CancellationToken>()).Returns(SampleFacts());
        var outcomeSignals = new OutcomeSignalPublisher(
            _facts, _signals, _ids, SignalWeights.Default, NullLogger<OutcomeSignalPublisher>.Instance);
        return new OwnerActionHandler(
            _repo, _ids, ReminderPolicy.Default, outcomeSignals, NullLogger<OwnerActionHandler>.Instance);
    }

    private Task Handle(OwnerActionRecorded message) =>
        CreateHandler().Handle(message, _bus, CancellationToken.None);

    private static OwnerActionRecorded Action(string action, DateTimeOffset at) =>
        new(Job, action, ChatId, at);

    private async Task<List<ApplicationStatusChanged>> CapturePublished()
    {
        var published = new List<ApplicationStatusChanged>();
        await _bus.PublishAsync(Arg.Do<ApplicationStatusChanged>(m => published.Add(m)));
        return published;
    }

    [Fact]
    public async Task A_first_action_on_an_unknown_job_creates_the_application_then_advances_it()
    {
        var published = await CapturePublished();

        await Handle(Action(OwnerActionRecorded.Save, T0));

        var app = _repo.Single();
        app.JobId.ShouldBe(Job);
        app.Status.ShouldBe(ApplicationStatus.Saved);
        // The creating New transition plus the New -> Saved the action drove.
        app.Transitions.Count.ShouldBe(2);
        app.Transitions[0].To.ShouldBe(ApplicationStatus.New);
        app.Transitions[1].From.ShouldBe(ApplicationStatus.New);
        app.Transitions[1].To.ShouldBe(ApplicationStatus.Saved);
        app.Transitions[1].Source.ShouldBe(TransitionSource.Telegram);
        _repo.SaveCount.ShouldBe(1);

        var change = published.ShouldHaveSingleItem();
        change.ApplicationId.ShouldBe(app.Id);
        change.JobId.ShouldBe(Job);
        change.FromStatus.ShouldBe(nameof(ApplicationStatus.New));
        change.ToStatus.ShouldBe(nameof(ApplicationStatus.Saved));
        change.OccurredAt.ShouldBe(T0);
    }

    [Fact]
    public async Task An_action_on_an_existing_application_advances_it_without_recreating()
    {
        var existing = App.Create(_ids.NewId(), Job, T0, TransitionSource.Telegram);
        existing.ChangeStatus(ApplicationStatus.Saved, TransitionSource.Telegram, T0.AddDays(1), ReminderPolicy.Default);
        _repo.Seed(existing);
        var published = await CapturePublished();

        await Handle(Action(OwnerActionRecorded.Applied, T0.AddDays(2)));

        _repo.Count.ShouldBe(1);
        existing.Status.ShouldBe(ApplicationStatus.Applied);
        existing.AppliedAt.ShouldBe(T0.AddDays(2));
        existing.Transitions.Count.ShouldBe(3);
        var change = published.ShouldHaveSingleItem();
        change.FromStatus.ShouldBe(nameof(ApplicationStatus.Saved));
        change.ToStatus.ShouldBe(nameof(ApplicationStatus.Applied));
    }

    [Fact]
    public async Task A_refused_transition_leaves_the_application_and_history_unchanged_and_publishes_nothing()
    {
        // Rejected -> Saved is refused by the contract matrix; a Save tap on a rejected application is a no-op.
        var existing = App.Create(_ids.NewId(), Job, T0, TransitionSource.Telegram);
        existing.ChangeStatus(ApplicationStatus.Rejected, TransitionSource.Telegram, T0.AddDays(1), ReminderPolicy.Default);
        _repo.Seed(existing);
        var published = await CapturePublished();

        await Handle(Action(OwnerActionRecorded.Save, T0.AddDays(2)));

        existing.Status.ShouldBe(ApplicationStatus.Rejected);
        existing.Transitions.Count.ShouldBe(2);
        _repo.SaveCount.ShouldBe(0);
        published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_identical_actions_produce_one_transition_and_one_published_change()
    {
        var published = await CapturePublished();
        var message = Action(OwnerActionRecorded.Save, T0);

        await Handle(message);
        await Handle(message);

        var app = _repo.Single();
        // New (creating) + New -> Saved. The redelivered identical action carries the same
        // (application_id, to_status, occurred_at) key and appends no second transition (SAD §8).
        app.Transitions.Count.ShouldBe(2);
        published.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_second_tap_a_moment_later_records_a_further_step_it_is_not_the_same_action()
    {
        var published = await CapturePublished();

        await Handle(Action(OwnerActionRecorded.Save, T0));
        await Handle(Action(OwnerActionRecorded.Applied, T0.AddSeconds(1)));

        var app = _repo.Single();
        app.Status.ShouldBe(ApplicationStatus.Applied);
        app.Transitions.Count.ShouldBe(3);
        published.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Open_is_not_a_pipeline_action_so_it_creates_nothing_and_publishes_nothing()
    {
        var published = await CapturePublished();

        await Handle(Action(OwnerActionRecorded.Open, T0));

        _repo.Count.ShouldBe(0);
        _repo.SaveCount.ShouldBe(0);
        published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Marking_applied_records_a_transition_and_publishes_only_the_status_change()
    {
        // Invariant 7: the system never applies for the Owner. Setting Applied has exactly two effects — a
        // history row and the status-change event — and no outbound action. The handler has no notifier or
        // HTTP dependency to make one; the only bus interaction is the single ApplicationStatusChanged.
        var published = await CapturePublished();

        await Handle(Action(OwnerActionRecorded.Applied, T0));

        var app = _repo.Single();
        app.Status.ShouldBe(ApplicationStatus.Applied);
        app.AppliedAt.ShouldBe(T0);
        published.ShouldHaveSingleItem();
        await _bus.Received(1).PublishAsync(Arg.Any<ApplicationStatusChanged>());
        _bus.ReceivedCalls().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Reaching_an_outcome_stages_a_weighted_signal_in_the_same_unit_of_work_as_the_transition()
    {
        // T08 / AC-08: the Applied transition and its signal are staged together, so the single SaveChanges
        // commits both in one transaction. The signal carries the outcome weight and the application it came
        // from, with the job's facts snapshotted at that moment.
        await Handle(Action(OwnerActionRecorded.Applied, T0));

        var app = _repo.Single();
        var signal = _signals.Staged.ShouldHaveSingleItem();
        signal.Kind.ShouldBe(SignalKind.Applied);
        signal.Weight.ShouldBe(SignalWeights.Default.Applied);
        signal.JobId.ShouldBe(Job);
        signal.ApplicationId.ShouldBe(app.Id);
        signal.OccurredAt.ShouldBe(T0);
        signal.JobFacts.ShouldBe(SampleFacts());
        // Staged before the one commit — the signal and the transition are one unit of work (done-when 3).
        _repo.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_non_outcome_action_stages_no_signal()
    {
        // Save is a card-action signal (F5's job), not an F6 outcome — F6 stages nothing for it.
        await Handle(Action(OwnerActionRecorded.Save, T0));

        _signals.Staged.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_transition_stages_no_signal()
    {
        var existing = App.Create(_ids.NewId(), Job, T0, TransitionSource.Telegram);
        existing.ChangeStatus(ApplicationStatus.Interview, TransitionSource.Telegram, T0.AddDays(1), ReminderPolicy.Default);
        _repo.Seed(existing);

        // Applied on an Interview application is refused by the matrix (no going backwards through the funnel);
        // nothing changes, so nothing is staged.
        await Handle(Action(OwnerActionRecorded.Applied, T0.AddDays(2)));

        _signals.Staged.ShouldBeEmpty();
    }

    /// <summary>
    /// A stateful in-memory <see cref="IApplicationRepository"/> double: it holds the aggregates added and
    /// returns the same instance by job id, so a redelivered action loads the application it created. It is a
    /// test double, not a mock of the handler — the two-call idempotency and lazy-create paths need real
    /// cross-call state a substitute cannot express cleanly.
    /// </summary>
    private sealed class FakeApplicationRepository : IApplicationRepository
    {
        private readonly List<App> _applications = [];

        public int Count => _applications.Count;

        public int SaveCount { get; private set; }

        public App Single() => _applications.ShouldHaveSingleItem();

        public void Seed(App application) => _applications.Add(application);

        public void Add(App application) => _applications.Add(application);

        public Task<App?> FindByJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_applications.Find(a => a.JobId == jobId));

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// A stateful <see cref="IOutcomeSignalWriter"/> double recording what the publisher staged, so the handler
    /// test can assert the outcome signal without a database. The real EF writer shares the handler's context,
    /// so the signal commits in the same transaction as the transition — proven in the integration suite.
    /// </summary>
    private sealed class FakeOutcomeSignalWriter : IOutcomeSignalWriter
    {
        public List<Signal> Staged { get; } = [];

        public bool IsStaged(Guid jobId, SignalKind kind, DateTimeOffset occurredAt) =>
            Staged.Any(s => s.JobId == jobId && s.Kind == kind && s.OccurredAt == occurredAt);

        public void Stage(Signal signal) => Staged.Add(signal);
    }
}
