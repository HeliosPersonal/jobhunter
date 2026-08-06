using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Preferences;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// T09: the shared status-change handler both the API <c>POST …/status</c> and the Telegram
/// <c>/pipeline</c> callbacks drive. Unlike <see cref="OwnerActionHandler"/> — which consumes a Wolverine
/// event on the pipeline host and publishes <see cref="Contracts.Pipeline.ApplicationStatusChanged"/> — this
/// one is invoked directly by a request-driven host that has no message bus (the F4 <c>ReMatchScheduler</c>
/// precedent), so it returns a value-typed <see cref="ChangeApplicationStatusOutcome"/> the caller renders.
///
/// <para>It evaluates the transition against <see cref="TransitionRules"/> (a refusal names the remedy, AC-10),
/// records the source the caller passed (done-when 4 — API and Telegram differ), and stages the T08 weighted
/// outcome signal into the same unit of work through <see cref="OutcomeSignalPublisher"/>, so an API-driven
/// Interview is F7 evidence exactly as a Telegram one is. It requires an existing application — a status change
/// annotates a tracked job, it does not lazily create one (like <see cref="AddNoteHandler"/>).</para>
/// </summary>
public sealed class ChangeApplicationStatusHandlerTests
{
    private static readonly Guid Job = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset T0 = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private readonly FakeApplicationRepository _repo = new();
    private readonly SequentialIdGenerator _ids = new();
    private readonly IJobFactsSnapshotQuery _facts = Substitute.For<IJobFactsSnapshotQuery>();
    private readonly FakeOutcomeSignalWriter _signals = new();

    private static JobFacts SampleFacts() => JobFacts.Create(new Dictionary<Dimension, IReadOnlyList<string>>
    {
        [Dimension.Country] = ["DE"],
    });

    private ChangeApplicationStatusHandler CreateHandler()
    {
        _facts.SnapshotAsync(Job, Arg.Any<CancellationToken>()).Returns(SampleFacts());
        var outcomeSignals = new OutcomeSignalPublisher(
            _facts, _signals, _ids, SignalWeights.Default, NullLogger<OutcomeSignalPublisher>.Instance);
        return new ChangeApplicationStatusHandler(
            _repo, outcomeSignals, ReminderPolicy.Default, NullLogger<ChangeApplicationStatusHandler>.Instance);
    }

    private Task<ChangeApplicationStatusOutcome> Handle(
        ApplicationStatus to, TransitionSource source, DateTimeOffset at, string? detail = null) =>
        CreateHandler().Handle(new ChangeApplicationStatusCommand(Job, to, source, at, detail), CancellationToken.None);

    private App Seed(ApplicationStatus status)
    {
        var app = App.Create(_ids.NewId(), Job, T0, TransitionSource.Telegram);
        if (status != ApplicationStatus.New)
        {
            app.ChangeStatus(status, TransitionSource.Telegram, T0.AddMinutes(1), ReminderPolicy.Default);
        }

        _repo.Seed(app);
        return app;
    }

    [Fact]
    public async Task A_permitted_change_advances_the_application_and_reports_the_move()
    {
        var app = Seed(ApplicationStatus.Saved);

        var outcome = await Handle(ApplicationStatus.Interview, TransitionSource.Api, T0.AddDays(1));

        outcome.Result.ShouldBe(ChangeApplicationStatusResult.Changed);
        outcome.ApplicationId.ShouldBe(app.Id);
        outcome.From.ShouldBe(ApplicationStatus.Saved);
        outcome.To.ShouldBe(ApplicationStatus.Interview);
        app.Status.ShouldBe(ApplicationStatus.Interview);
        _repo.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_change_names_the_rule_and_the_remedy_and_changes_nothing()
    {
        // AC-10: Rejected -> Interview is refused; the outcome carries the from/to pair and the remedy verbatim,
        // which is exactly the contract's 409 body. Nothing is written.
        var app = Seed(ApplicationStatus.Rejected);

        var outcome = await Handle(ApplicationStatus.Interview, TransitionSource.Api, T0.AddDays(1));

        outcome.Result.ShouldBe(ChangeApplicationStatusResult.NotPermitted);
        outcome.From.ShouldBe(ApplicationStatus.Rejected);
        outcome.To.ShouldBe(ApplicationStatus.Interview);
        outcome.Remedy.ShouldNotBeNull();
        outcome.Remedy.ShouldContain("cannot return to Interview after Rejected");
        app.Status.ShouldBe(ApplicationStatus.Rejected);
        _repo.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_change_for_an_untracked_job_reports_not_found_and_writes_nothing()
    {
        var outcome = await Handle(ApplicationStatus.Applied, TransitionSource.Api, T0);

        outcome.Result.ShouldBe(ChangeApplicationStatusResult.ApplicationNotFound);
        _repo.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_source_the_caller_passes_is_recorded_on_the_transition()
    {
        // done-when 4: a change from the API records Api; the same change from Telegram records Telegram.
        Seed(ApplicationStatus.Saved);
        var app = _repo.Single();

        await Handle(ApplicationStatus.Applied, TransitionSource.Api, T0.AddDays(1));

        app.Transitions[^1].Source.ShouldBe(TransitionSource.Api);
        app.Transitions[^1].To.ShouldBe(ApplicationStatus.Applied);
    }

    [Fact]
    public async Task An_optional_detail_is_recorded_on_the_transition()
    {
        Seed(ApplicationStatus.Applied);
        var app = _repo.Single();

        await Handle(ApplicationStatus.Interview, TransitionSource.Api, T0.AddDays(1), "first call scheduled");

        app.Transitions[^1].Detail.ShouldBe("first call scheduled");
    }

    [Fact]
    public async Task Reaching_an_outcome_stages_a_weighted_signal_in_the_same_unit_of_work()
    {
        // T08 reuse: an API-driven outcome is F7 evidence exactly as a Telegram one is — the signal is staged
        // before the one SaveChanges, so it commits with the transition.
        Seed(ApplicationStatus.Applied);

        await Handle(ApplicationStatus.Interview, TransitionSource.Api, T0.AddDays(1));

        var signal = _signals.Staged.ShouldHaveSingleItem();
        signal.Kind.ShouldBe(SignalKind.Interview);
        signal.Weight.ShouldBe(SignalWeights.Default.Interview);
        signal.JobId.ShouldBe(Job);
        _repo.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_non_outcome_change_stages_no_signal()
    {
        // Saved is F5's card-action signal, not an F6 outcome — F6 stages nothing for it.
        Seed(ApplicationStatus.New);

        await Handle(ApplicationStatus.Saved, TransitionSource.Api, T0.AddDays(1));

        _signals.Staged.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_change_stages_no_signal()
    {
        Seed(ApplicationStatus.Rejected);

        await Handle(ApplicationStatus.Interview, TransitionSource.Api, T0.AddDays(1));

        _signals.Staged.ShouldBeEmpty();
    }

    private sealed class FakeApplicationRepository : IApplicationRepository
    {
        private readonly List<App> _applications = [];

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

    private sealed class FakeOutcomeSignalWriter : IOutcomeSignalWriter
    {
        public List<Signal> Staged { get; } = [];

        public bool IsStaged(Guid jobId, SignalKind kind, DateTimeOffset occurredAt) =>
            Staged.Any(s => s.JobId == jobId && s.Kind == kind && s.OccurredAt == occurredAt);

        public void Stage(Signal signal) => Staged.Add(signal);
    }
}
