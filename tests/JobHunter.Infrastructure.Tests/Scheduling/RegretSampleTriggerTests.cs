using JobHunter.Application.Ratings;
using JobHunter.Infrastructure.Scheduling;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wolverine;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Scheduling;

/// <summary>
/// F4 T21 (done-when 1): the Hangfire body for the weekly regret sample. It carries no sampling logic — that
/// lives in the <c>RegretSampler</c> Application handler, unit-tested without Hangfire — so all it must do is
/// stamp the due instant from <see cref="IClock"/> and publish exactly one <see cref="RegretSampleDue"/> onto
/// the durable bus. Stamping the instant here (not in the handler) is what makes a redelivered tick resolve the
/// same week and open the same sample, so the cheap-tier match is never run — or paid for — twice.
/// </summary>
public sealed class RegretSampleTriggerTests
{
    [Fact]
    public async Task It_publishes_one_regret_sample_message_stamped_with_the_clock_instant()
    {
        var now = new DateTimeOffset(2026, 8, 10, 6, 30, 0, TimeSpan.Zero);  // a Monday 09:30 Kyiv in UTC
        var bus = Substitute.For<IMessageBus>();
        var trigger = new RegretSampleTrigger(
            bus, new FakeClock(now), NullLogger<RegretSampleTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<RegretSampleDue>(m => m != null && m.DueAt == now));
    }
}
