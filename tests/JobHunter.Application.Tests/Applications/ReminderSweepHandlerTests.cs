using JobHunter.Application.Applications;
using JobHunter.Application.Delivery;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Notifications;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using App = JobHunter.Domain.Applications.Application;
using DeliveryOptions = JobHunter.Application.Delivery.DeliveryOptions;

namespace JobHunter.Application.Tests.Applications;

/// <summary>
/// T06: the reminder sweep sends one nudge per un-suppressed due application and records it, so the next
/// sweep for the same condition stays silent (QG-3, done-when 1). The due read is substituted, the aggregate
/// mutation runs through the real <c>FindByJobAsync</c> → <c>RecordReminder</c> → <c>SaveChangesAsync</c>
/// path (no update/delete on the repository, QG-1), and the renderer/notifier are doubles — so these are
/// zero-database unit tests. The seven-day suppression proof over a real database lives in the Infrastructure
/// integration suite (AC-05).
/// </summary>
public sealed class ReminderSweepHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 8, 0, 0, TimeSpan.Zero);
    private const long ChatId = 4242;

    private readonly IDueReminderQuery _due = Substitute.For<IDueReminderQuery>();
    private readonly FakeApplicationRepository _repo = new();
    private readonly IReminderRenderer _renderer = Substitute.For<IReminderRenderer>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    public ReminderSweepHandlerTests() =>
        _renderer.Render(Arg.Any<DueReminder>()).Returns(RenderedMessage.PlainText("nudge"));

    private ReminderSweepHandler CreateHandler() =>
        new(_due, _repo, _renderer, _notifier, ReminderPolicy.Default,
            new DeliveryOptions { OwnerChatId = ChatId }, NullLogger<ReminderSweepHandler>.Instance);

    private Task Handle() =>
        CreateHandler().Handle(new ReminderSweepDue(Now), _bus, CancellationToken.None);

    private App SeedApplied(Guid jobId, DateTimeOffset appliedAt)
    {
        var app = App.Create(Guid.CreateVersion7(), jobId, appliedAt.AddMinutes(-1), TransitionSource.Telegram);
        app.ChangeStatus(ApplicationStatus.Applied, TransitionSource.Telegram, appliedAt, ReminderPolicy.Default);
        _repo.Seed(app);
        return app;
    }

    private static DueReminder Due(Guid jobId, ApplicationStatus status, string? lastCondition = null) =>
        new(Guid.CreateVersion7(), jobId, "Staff SRE", "Acme", "https://acme.test/apply", status, false, lastCondition);

    [Fact]
    public async Task It_sends_one_nudge_per_due_application_and_records_the_reminder()
    {
        var job = Guid.CreateVersion7();
        var app = SeedApplied(job, Now.AddDays(-11));
        _due.DueAsync(Now, Arg.Any<CancellationToken>()).Returns([Due(job, ApplicationStatus.Applied)]);

        await Handle();

        await _notifier.Received(1).SendAsync(ChatId, Arg.Any<RenderedMessage>(), Arg.Any<CancellationToken>());
        app.LastReminderCondition.ShouldBe("stale:Applied");
        app.LastReminderAt.ShouldBe(Now);
        _repo.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_already_reminded_application_is_skipped()
    {
        var job = Guid.CreateVersion7();
        SeedApplied(job, Now.AddDays(-11));
        // The last reminder already fired for this exact condition — suppressed until it clears or recurs.
        _due.DueAsync(Now, Arg.Any<CancellationToken>())
            .Returns([Due(job, ApplicationStatus.Applied, lastCondition: "stale:Applied")]);

        await Handle();

        await _notifier.DidNotReceive().SendAsync(Arg.Any<long>(), Arg.Any<RenderedMessage>(), Arg.Any<CancellationToken>());
        _repo.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_sweep_with_no_due_applications_sends_nothing()
    {
        _due.DueAsync(Now, Arg.Any<CancellationToken>()).Returns([]);

        await Handle();

        await _notifier.DidNotReceive().SendAsync(Arg.Any<long>(), Arg.Any<RenderedMessage>(), Arg.Any<CancellationToken>());
        _repo.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Recording_a_reminder_pushes_next_action_forward_so_the_next_sweep_is_quiet()
    {
        var job = Guid.CreateVersion7();
        var app = SeedApplied(job, Now.AddDays(-11));
        _due.DueAsync(Now, Arg.Any<CancellationToken>()).Returns([Due(job, ApplicationStatus.Applied)]);

        await Handle();

        // Applied threshold is 10 days: the reminder pushes next_action_at ten days past the sweep instant.
        app.NextActionAt.ShouldBe(Now.AddDays(10));
    }

    /// <summary>
    /// A stateful in-memory <see cref="IApplicationRepository"/> double, mirroring the owner-action tests: it
    /// returns the seeded aggregate by job id so the sweep mutates the same instance it later saves.
    /// </summary>
    private sealed class FakeApplicationRepository : IApplicationRepository
    {
        private readonly List<App> _applications = [];

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
