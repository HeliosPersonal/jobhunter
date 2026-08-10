using JobHunter.Application.Applications;
using JobHunter.Application.Discovery;
using JobHunter.Application.Enrichment;
using JobHunter.Application.Lifecycle;
using JobHunter.Application.Reporting;
using JobHunter.Application.Delivery;
using JobHunter.Infrastructure.Scheduling;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wolverine;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Scheduling;

/// <summary>
/// The thin Hangfire publish-triggers (SAD §6.1–6.3). Each carries no stage logic — that lives in an
/// Application handler unit-tested without Hangfire — so the only behaviour to pin is: stamp the instant
/// from <see cref="IClock"/> and publish exactly one message onto the durable bus. Stamping in the trigger
/// (not the handler) is what makes a redelivered tick resolve the same window, so the downstream stage stays
/// idempotent. These mirror the existing <see cref="WeeklyRatingTriggerTests"/> / <see cref="RegretSampleTriggerTests"/>.
/// </summary>
public sealed class PipelineTriggersTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DailyRunTrigger_publishes_one_StartDailyRun_stamped_with_the_clock_instant()
    {
        var bus = Substitute.For<IMessageBus>();
        var trigger = new DailyRunTrigger(bus, new FakeClock(Now), NullLogger<DailyRunTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<StartDailyRun>(m => m != null && m.WindowEnd == Now));
    }

    [Fact]
    public async Task DigestAssemblyTrigger_publishes_one_DigestAssemblyDue_stamped_with_the_clock_instant()
    {
        var bus = Substitute.For<IMessageBus>();
        var trigger = new DigestAssemblyTrigger(bus, new FakeClock(Now), NullLogger<DigestAssemblyTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<DigestAssemblyDue>(m => m != null && m.DueAt == Now));
    }

    [Fact]
    public async Task DigestDeliveryTrigger_publishes_one_DigestDeliveryDue_stamped_with_the_clock_instant()
    {
        var bus = Substitute.For<IMessageBus>();
        var trigger = new DigestDeliveryTrigger(bus, new FakeClock(Now), NullLogger<DigestDeliveryTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<DigestDeliveryDue>(m => m != null && m.DueAt == Now));
    }

    [Fact]
    public async Task DiscoveryCycleTrigger_publishes_one_DiscoveryCycleDue_stamped_with_the_clock_instant()
    {
        var bus = Substitute.For<IMessageBus>();
        var trigger = new DiscoveryCycleTrigger(bus, new FakeClock(Now), NullLogger<DiscoveryCycleTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<DiscoveryCycleDue>(m => m != null && m.WindowStart == Now));
    }

    [Fact]
    public async Task ClosureSweepTrigger_publishes_one_ClosureSweepDue_stamped_with_the_clock_instant()
    {
        var bus = Substitute.For<IMessageBus>();
        var trigger = new ClosureSweepTrigger(bus, new FakeClock(Now), NullLogger<ClosureSweepTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<ClosureSweepDue>(m => m != null && m.WindowStart == Now));
    }

    [Fact]
    public async Task RedetectBindingTrigger_publishes_one_RedetectBindingsDue_stamped_with_the_clock_instant()
    {
        var bus = Substitute.For<IMessageBus>();
        var trigger = new RedetectBindingTrigger(bus, new FakeClock(Now), NullLogger<RedetectBindingTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<RedetectBindingsDue>(m => m != null && m.WindowStart == Now));
    }

    [Fact]
    public async Task ReminderSweepTrigger_publishes_one_ReminderSweepDue_stamped_with_the_clock_instant()
    {
        var bus = Substitute.For<IMessageBus>();
        var trigger = new ReminderSweepTrigger(bus, new FakeClock(Now), NullLogger<ReminderSweepTrigger>.Instance);

        await trigger.PublishAsync();

        await bus.Received(1).PublishAsync(Arg.Is<ReminderSweepDue>(m => m != null && m.SweptAt == Now));
    }

    [Fact]
    public async Task JobLivenessCheckTrigger_publishes_one_JobLivenessCheckDue_stamped_two_cycles_before_now()
    {
        var bus = Substitute.For<IMessageBus>();
        var trigger = new JobLivenessCheckTrigger(bus, new FakeClock(Now), NullLogger<JobLivenessCheckTrigger>.Instance);

        await trigger.PublishAsync();

        // The cutoff is two six-hourly discovery cycles (12h) before the clock instant: a job unseen this
        // long is stale across every source (SAD §6.2, T08).
        var expectedCutoff = Now - TimeSpan.FromHours(12);
        await bus.Received(1).PublishAsync(Arg.Is<JobLivenessCheckDue>(m => m != null && m.SeenBefore == expectedCutoff));
    }
}
