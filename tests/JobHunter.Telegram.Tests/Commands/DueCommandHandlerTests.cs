using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using JobHunter.Domain.Notifications;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/due</c> (contract §Commands, F6 SAD §6.2): the applications past their stage threshold, each with a
/// suggested action — the pull version of the 08:00 reminder sweep. It reads the due set through
/// <see cref="IDueReminderQuery"/> as of the injected <see cref="IClock"/> — never <c>DateTime.Now</c> — and
/// renders each through the one shared <see cref="IReminderRenderer"/>, so a pulled nudge reads exactly like
/// a pushed one. An empty result is one plain, helpful line; the CV is nowhere near it.
/// </summary>
public sealed class DueCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly IDueReminderQuery _due = Substitute.For<IDueReminderQuery>();
    private readonly IReminderRenderer _renderer = Substitute.For<IReminderRenderer>();
    private readonly FakeClock _clock = new(Now);

    private DueCommandHandler NewHandler() =>
        new(_due, _renderer, _clock, NullLogger<DueCommandHandler>.Instance);

    private static DueReminder Reminder(string title) => new(
        Guid.NewGuid(), Guid.NewGuid(), title, "Acme", "https://acme.example/jobs/1",
        ApplicationStatus.Applied, PostingClosed: false, LastReminderCondition: null);

    [Fact]
    public async Task It_renders_one_nudge_per_due_application_through_the_shared_renderer()
    {
        _due.DueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([Reminder("Senior SRE"), Reminder("Staff Backend Engineer")]);
        _renderer.Render(Arg.Any<DueReminder>())
            .Returns(ci => RenderedMessage.PlainText("nudge: " + ci.Arg<DueReminder>()!.Title));

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // Every due application becomes exactly one message, and each goes through the sweep's own renderer so
        // the pull reads like the push (no second layout).
        messages.Count.ShouldBe(2);
        messages.ShouldContain(m => m.Text.Contains("Senior SRE"));
        messages.ShouldContain(m => m.Text.Contains("Staff Backend Engineer"));
        _renderer.Received(2).Render(Arg.Any<DueReminder>());
    }

    [Fact]
    public async Task It_reads_the_due_set_as_of_the_clock()
    {
        _due.DueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // Due-ness is measured against the caller's clock, never DateTime.Now (coding-standards §IClock).
        await _due.Received(1).DueAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_result_yields_one_plain_helpful_line()
    {
        _due.DueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);

        var messages = await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        messages.ShouldHaveSingleItem().Text.ShouldContain("nothing", Case.Insensitive);
        _renderer.DidNotReceive().Render(Arg.Any<DueReminder>());
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => NewHandler().HandleAsync(null!));
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        Should.Throw<ArgumentNullException>(() =>
            new DueCommandHandler(null!, _renderer, _clock, NullLogger<DueCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new DueCommandHandler(_due, null!, _clock, NullLogger<DueCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new DueCommandHandler(_due, _renderer, null!, NullLogger<DueCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new DueCommandHandler(_due, _renderer, _clock, null!));
    }
}
