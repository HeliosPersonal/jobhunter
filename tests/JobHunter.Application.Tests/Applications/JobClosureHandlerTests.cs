using JobHunter.Application.Applications;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// T05: the job-closure handler consumes <see cref="JobClosed"/> (F1/F2) and marks the tracked application's
/// posting closed — without changing the status (AC-07). A closed posting tells us nothing about the Owner's
/// application; auto-rejecting would fabricate an outcome and poison F7's evidence (SAD §6.3). The closure is
/// still recorded as history — a <see cref="TransitionSource.System"/> self-transition — so the event is
/// visible without pretending a move happened.
///
/// <para>Closure is a no-op for a terminal or non-existent application, and idempotent — a redelivered
/// closure changes nothing further. It publishes nothing: the status did not change, so no
/// <see cref="ApplicationStatusChanged"/> is emitted. The repository is the same small stateful fake the
/// owner-action suite uses, so the load/mark/save and idempotency paths are exercised without a database.</para>
/// </summary>
public sealed class JobClosureHandlerTests
{
    private static readonly Guid Job = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AppId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset T0 = new(2026, 8, 6, 7, 0, 0, TimeSpan.Zero);

    private readonly FakeApplicationRepository _repo = new();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private JobClosureHandler CreateHandler() =>
        new(_repo, NullLogger<JobClosureHandler>.Instance);

    private Task Handle(DateTimeOffset closedAt) =>
        CreateHandler().Handle(new JobClosed(Job, closedAt, "StaleAcrossAllSources", closedAt), _bus, CancellationToken.None);

    private async Task<List<object>> CapturePublished()
    {
        var published = new List<object>();
        await _bus.PublishAsync(Arg.Do<object>(m => published.Add(m)));
        return published;
    }

    private static App Existing(ApplicationStatus status)
    {
        var app = App.Create(AppId, Job, T0, TransitionSource.Telegram);
        if (status != ApplicationStatus.New)
        {
            app.ChangeStatus(status, TransitionSource.Telegram, T0.AddMinutes(1), ReminderPolicy.Default);
        }

        return app;
    }

    [Fact]
    public async Task A_closure_marks_the_posting_and_records_a_system_self_transition_without_changing_status()
    {
        var app = Existing(ApplicationStatus.Applied);
        _repo.Seed(app);
        var published = await CapturePublished();

        await Handle(T0.AddDays(1));

        app.PostingClosed.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Applied);
        var closure = app.Transitions[^1];
        closure.From.ShouldBe(ApplicationStatus.Applied);
        closure.To.ShouldBe(ApplicationStatus.Applied);
        closure.Source.ShouldBe(TransitionSource.System);
        closure.Detail.ShouldBe(App.PostingClosedDetail);
        _repo.SaveCount.ShouldBe(1);
        // The status did not change, so nothing is published (SAD §6.3).
        published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_closure_for_a_terminal_application_is_a_no_op()
    {
        var app = Existing(ApplicationStatus.Rejected);
        var transitionsBefore = app.Transitions.Count;
        _repo.Seed(app);
        var published = await CapturePublished();

        await Handle(T0.AddDays(1));

        // The outcome is already known — closing changes nothing (SAD §6.3).
        app.PostingClosed.ShouldBeFalse();
        app.Transitions.Count.ShouldBe(transitionsBefore);
        _repo.SaveCount.ShouldBe(0);
        published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_closure_for_an_untracked_job_is_a_no_op_not_an_error()
    {
        var published = await CapturePublished();

        await Handle(T0.AddDays(1));

        _repo.Count.ShouldBe(0);
        _repo.SaveCount.ShouldBe(0);
        published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_redelivered_closure_changes_nothing_further()
    {
        var app = Existing(ApplicationStatus.Applied);
        _repo.Seed(app);

        await Handle(T0.AddDays(1));
        await Handle(T0.AddDays(2));

        app.PostingClosed.ShouldBeTrue();
        // Idempotent: exactly one closure transition, and last_activity_at reflects the first close only.
        app.Transitions.Count(t => t.Detail == App.PostingClosedDetail).ShouldBe(1);
        app.LastActivityAt.ShouldBe(T0.AddDays(1));
    }

    /// <summary>
    /// The same stateful in-memory <see cref="IApplicationRepository"/> double the owner-action suite uses: it
    /// holds the aggregates added and returns the same instance by job id, so a redelivered closure loads the
    /// application the first closure marked.
    /// </summary>
    private sealed class FakeApplicationRepository : IApplicationRepository
    {
        private readonly List<App> _applications = [];

        public int Count => _applications.Count;

        public int SaveCount { get; private set; }

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
}
