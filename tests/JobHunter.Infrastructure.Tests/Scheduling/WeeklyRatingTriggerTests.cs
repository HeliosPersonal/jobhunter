using JobHunter.Application.Ratings;
using JobHunter.Infrastructure.Scheduling;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wolverine;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Scheduling;

/// <summary>
/// F4 T20 (done-when 1, 5): the Hangfire body for the weekly rating prompt. It carries no rating logic — that
/// lives in the <c>WeeklyRatingHandler</c> Application handler, unit-tested without Hangfire — so all it must
/// do is stamp the due instant from <see cref="IClock"/> and publish exactly one <see cref="WeeklyRatingDue"/>
/// onto the durable bus. Stamping the instant here (not in the handler) is what makes a redelivered tick read
/// the same seven-day window and open the same round, so precision@10 is never double-counted.
/// </summary>
public sealed class WeeklyRatingTriggerTests
{
    [Fact]
    public async Task It_publishes_one_weekly_rating_message_stamped_with_the_clock_instant()
    {
        var now = new DateTimeOffset(2026, 8, 10, 6, 0, 0, TimeSpan.Zero);  // a Monday 09:00 Kyiv in UTC
        var bus = Substitute.For<IMessageBus>();
        var trigger = new WeeklyRatingTrigger(
            bus, new FakeClock(now), NullLogger<WeeklyRatingTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<WeeklyRatingDue>(m => m != null && m.DueAt == now));
    }
}
