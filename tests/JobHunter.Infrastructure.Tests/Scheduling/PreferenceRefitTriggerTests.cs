using JobHunter.Application.Preferences;
using JobHunter.Infrastructure.Scheduling;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wolverine;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Scheduling;

/// <summary>
/// F7 T05: the Hangfire body for the weekly refit. It carries no fitting logic — that lives in the
/// <c>PreferenceLearner</c> Application handler, unit-tested without Hangfire — so all it must do is stamp the
/// refit instant from <see cref="IClock"/> and publish exactly one <see cref="RecomputePreferencesDue"/> onto
/// the durable bus. Stamping the instant here (not in the handler) is what makes a redelivered tick read the
/// same 180-day window and fit the same model.
/// </summary>
public sealed class PreferenceRefitTriggerTests
{
    [Fact]
    public async Task It_publishes_one_recompute_message_stamped_with_the_clock_instant()
    {
        var now = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);  // a Monday 03:00 Kyiv in UTC
        var bus = Substitute.For<IMessageBus>();
        var trigger = new PreferenceRefitTrigger(
            bus, new FakeClock(now), NullLogger<PreferenceRefitTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<RecomputePreferencesDue>(m => m != null && m.FittedAt == now));
    }
}
