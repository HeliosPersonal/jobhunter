using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Commands;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Commands;

/// <summary>
/// <c>/stats</c> (contract §Commands, T11 AC): this week's engagement — delivered, opened, ignored, saved,
/// applied — with a precision figure and a week-over-week trend, in the same scannable form as the digest.
/// It reads two windows through <see cref="IWeeklyStatsQuery"/> (this week and the week before) and computes
/// the precision and the trend from them; the command path is deterministic — no LLM is ever resolved, and
/// the CV is nowhere near it. An empty week is a plain, helpful line, never an empty message.
/// </summary>
public sealed class StatsCommandHandlerTests
{
    private const long OwnerChat = 4242;

    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly IWeeklyStatsQuery _stats = Substitute.For<IWeeklyStatsQuery>();
    private readonly FakeClock _clock = new(Now);

    private StatsCommandHandler NewHandler() => new(_stats, _clock, NullLogger<StatsCommandHandler>.Instance);

    [Fact]
    public async Task It_reports_this_weeks_counts_in_one_message()
    {
        _stats.EngagementAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new WeeklyEngagement(Delivered: 20, Opened: 8, Ignored: 6, Saved: 3, Applied: 2));

        var message = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null))).ShouldHaveSingleItem();

        message.Text.ShouldContain("20");
        message.Text.ShouldContain("8");
        message.Text.ShouldContain("2");
    }

    [Fact]
    public async Task It_reads_this_week_and_the_week_before_as_two_adjacent_windows()
    {
        _stats.EngagementAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(WeeklyEngagement.Empty);

        await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null));

        // This week is [now-7d, now); the prior week is [now-14d, now-7d) — adjacent, half-open, no overlap.
        await _stats.Received(1).EngagementAsync(
            Now.AddDays(-7), Now, Arg.Any<CancellationToken>());
        await _stats.Received(1).EngagementAsync(
            Now.AddDays(-14), Now.AddDays(-7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_shows_precision_as_a_percentage_of_delivered_cards()
    {
        // 8 opened + 3 saved + 1 applied = 12 positive of 24 delivered → 50%.
        _stats.EngagementAsync(Now.AddDays(-7), Now, Arg.Any<CancellationToken>())
            .Returns(new WeeklyEngagement(Delivered: 24, Opened: 8, Ignored: 12, Saved: 3, Applied: 1));
        _stats.EngagementAsync(Now.AddDays(-14), Now.AddDays(-7), Arg.Any<CancellationToken>())
            .Returns(WeeklyEngagement.Empty);

        var message = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null))).ShouldHaveSingleItem();

        message.Text.ShouldContain("50%");
    }

    [Fact]
    public async Task It_shows_an_upward_trend_when_precision_beats_the_week_before()
    {
        // This week 50%, last week 25% → up.
        _stats.EngagementAsync(Now.AddDays(-7), Now, Arg.Any<CancellationToken>())
            .Returns(new WeeklyEngagement(Delivered: 10, Opened: 5, Ignored: 5, Saved: 0, Applied: 0));
        _stats.EngagementAsync(Now.AddDays(-14), Now.AddDays(-7), Arg.Any<CancellationToken>())
            .Returns(new WeeklyEngagement(Delivered: 8, Opened: 2, Ignored: 6, Saved: 0, Applied: 0));

        var message = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null))).ShouldHaveSingleItem();

        message.Text.ShouldContain("▲");
    }

    [Fact]
    public async Task An_empty_week_yields_one_plain_helpful_line()
    {
        _stats.EngagementAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(WeeklyEngagement.Empty);

        var message = (await NewHandler().HandleAsync(new CommandRequest(OwnerChat, null))).ShouldHaveSingleItem();

        message.Text.ShouldContain("nothing", Case.Insensitive);
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
            new StatsCommandHandler(null!, _clock, NullLogger<StatsCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new StatsCommandHandler(_stats, null!, NullLogger<StatsCommandHandler>.Instance));
        Should.Throw<ArgumentNullException>(() =>
            new StatsCommandHandler(_stats, _clock, null!));
    }
}
