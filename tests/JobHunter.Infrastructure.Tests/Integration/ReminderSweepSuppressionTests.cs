using JobHunter.Application.Applications;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Companies;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Postings;
using JobHunter.Domain.Sources;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Queries;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using App = JobHunter.Domain.Applications.Application;
using DeliveryOptions = JobHunter.Application.Delivery.DeliveryOptions;

namespace JobHunter.Infrastructure.Tests.Integration;

/// <summary>
/// T06 AC-05 / done-when 1, end to end over the real store: the sweep drives the real
/// <see cref="DueReminderQuery"/> and <see cref="ApplicationRepository"/>, so the suppression rule (QG-3 —
/// one reminder per <c>(application, condition)</c> until it clears or recurs) is asserted against the actual
/// <c>next_action_at</c> reschedule and the persisted last-reminder condition, not a double. Requires Docker.
/// </summary>
public sealed class ReminderSweepSuppressionTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly ReminderPolicy Policy = ReminderPolicy.Default;
    private const long OwnerChatId = 4242;

    [RequiresDockerFact]
    public async Task A_stale_application_is_reminded_once_over_seven_consecutive_daily_sweeps()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // Applied 11 days before the first sweep: past the 10-day threshold, so next_action_at is in the past.
        await PersistApplicationAsync(seed.Database, seed.JobId, ApplicationStatus.Applied, Start.AddDays(-11));

        var notifier = new RecordingNotifier();

        // Seven consecutive daily sweeps, one per morning at 08:00 (QG-3).
        for (var day = 0; day < 7; day++)
        {
            await SweepAsync(seed.Database, notifier, Start.AddDays(day));
        }

        // Exactly one nudge across the week: the first sweep fires it and pushes next_action_at ten days out,
        // so every later sweep in the window finds it already reminded for the same condition and stays quiet.
        notifier.Sends.Count.ShouldBe(1);
        notifier.Sends[0].ChatId.ShouldBe(OwnerChatId);
    }

    [RequiresDockerFact]
    public async Task An_owner_action_that_clears_the_condition_the_same_day_takes_the_reminder_away()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // Due on the sweep morning, but the Owner moves it to Interview the moment before — a new condition
        // with its own fresh threshold, so nothing is due yet and the sweep finds nothing to nudge.
        await PersistApplicationAsync(
            seed.Database, seed.JobId, ApplicationStatus.Applied, Start.AddDays(-11),
            thenMoveTo: (ApplicationStatus.Interview, Start.AddMinutes(-1)));

        var notifier = new RecordingNotifier();
        await SweepAsync(seed.Database, notifier, Start);

        notifier.Sends.ShouldBeEmpty();
    }

    [RequiresDockerFact]
    public async Task A_shortened_threshold_takes_effect_on_the_next_sweep_with_no_rescheduling()
    {
        var seed = await SeedAsync();
        await using var _ = seed.Database;

        // Applied 11 days ago and reminded on day 0 under the default 10-day threshold: next_action_at is now
        // ten days out. Two days later, the threshold is shortened to one day; because the sweep resolves the
        // policy at run time and the reschedule is only a property write, the already-reminded row is not
        // re-due until it recurs — a config change does not retroactively reschedule what is already parked.
        await PersistApplicationAsync(seed.Database, seed.JobId, ApplicationStatus.Applied, Start.AddDays(-11));

        var notifier = new RecordingNotifier();
        await SweepAsync(seed.Database, notifier, Start);
        notifier.Sends.Count.ShouldBe(1);

        var shortened = new ReminderPolicy(new Dictionary<ApplicationStatus, TimeSpan>
        {
            [ApplicationStatus.Applied] = TimeSpan.FromDays(1),
        });

        // Day 2 under the shortened policy: next_action_at is still ~8 days out from the day-0 reminder, so
        // the row is not due — the shortened threshold governs the *next* reschedule, not this parked one.
        await SweepAsync(seed.Database, notifier, Start.AddDays(2), shortened);
        notifier.Sends.Count.ShouldBe(1);
    }

    private static async Task SweepAsync(
        TestDatabase database, INotifier notifier, DateTimeOffset sweptAt, ReminderPolicy? policy = null)
    {
        var factory = new NpgsqlConnectionFactory(database.ConnectionString);
        await using var context = database.CreateContext();

        var renderer = Substitute.For<IReminderRenderer>();
        renderer.Render(Arg.Any<DueReminder>()).Returns(RenderedMessage.PlainText("nudge"));

        var handler = new ReminderSweepHandler(
            new DueReminderQuery(factory),
            new ApplicationRepository(context),
            renderer,
            notifier,
            policy ?? Policy,
            new DeliveryOptions { OwnerChatId = OwnerChatId },
            NullLogger<ReminderSweepHandler>.Instance);

        await handler.Handle(new ReminderSweepDue(sweptAt), Substitute.For<IMessageBus>(), CancellationToken.None);
    }

    private static async Task PersistApplicationAsync(
        TestDatabase database,
        Guid jobId,
        ApplicationStatus status,
        DateTimeOffset enteredAt,
        (ApplicationStatus To, DateTimeOffset At)? thenMoveTo = null)
    {
        var app = App.Create(jobId, jobId, enteredAt.AddMinutes(-1), TransitionSource.Telegram);
        app.ChangeStatus(status, TransitionSource.Telegram, enteredAt, Policy);
        if (thenMoveTo is { } move)
        {
            app.ChangeStatus(move.To, TransitionSource.Telegram, move.At, Policy);
        }

        await using var ctx = database.CreateContext();
        ctx.Add(app);
        await ctx.SaveChangesAsync();
    }

    private static async Task<Seed> SeedAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var companyId = Guid.CreateVersion7();

        await using (var ctx = database.CreateContext())
        {
            ctx.Add(new Company(companyId, CanonicalDomain.TryCreate("acme.com").Value, "Acme", CompanySource.Curated, Start));
            await ctx.SaveChangesAsync();
        }

        var jobId = await SeedJobAsync(database, companyId);
        return new Seed(database, jobId);
    }

    private static async Task<Guid> SeedJobAsync(TestDatabase database, Guid companyId)
    {
        var bindingId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        var rawPostingId = Guid.CreateVersion7();
        var jobId = Guid.CreateVersion7();

        await using var ctx = database.CreateContext();
        ctx.Add(new AtsBinding(bindingId, companyId, AtsKind.Greenhouse, $"acme-{jobId:N}", BindingConfidence.TryCreate(0.9m).Value, "{}", Start));
        ctx.Add(new JobSource(sourceId, companyId, bindingId, $"https://boards-api.greenhouse.io/v1/boards/acme-{jobId:N}/jobs"));
        ctx.Add(new RawPosting(rawPostingId, sourceId, $"job-{jobId:N}", ContentHash.Compute($"{{\"t\":\"{jobId:N}\"}}"), "{\"t\":\"x\"}", 200, Start));
        ctx.Add(new Job(
            jobId, companyId, rawPostingId, Fingerprint.TryCreate(jobId.ToString("N") + Guid.NewGuid().ToString("N")).Value,
            fingerprintVersion: 1, "Staff SRE", normalisedTitle: "staff sre", description: "d",
            applyUrl: $"https://acme.com/apply/{jobId:N}",
            LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]),
            RemotePolicy.Hybrid, EmploymentType.FullTime, PostedAtGranularity.Day,
            firstSeenAt: Start, lastSeenAt: Start, status: JobStatus.Live));
        await ctx.SaveChangesAsync();
        return jobId;
    }

    private sealed record Seed(TestDatabase Database, Guid JobId);

    private sealed class RecordingNotifier : INotifier
    {
        private long _nextMessageId = 1000;

        public List<(long ChatId, RenderedMessage Message)> Sends { get; } = [];

        public Task<long> SendAsync(long chatId, RenderedMessage message, CancellationToken cancellationToken = default)
        {
            Sends.Add((chatId, message));
            return Task.FromResult(_nextMessageId++);
        }
    }
}
