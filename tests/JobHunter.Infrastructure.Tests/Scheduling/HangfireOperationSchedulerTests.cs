using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using JobHunter.Infrastructure.Scheduling;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Infrastructure.Tests.Scheduling;

/// <summary>
/// The Hangfire-backed <see cref="Domain.Abstractions.IOperationScheduler"/> (F9 operational endpoints,
/// ADR-0004). Each method enqueues a job onto the shared PostgreSQL storage and returns the Hangfire job id
/// as the operation id the operator can quote. Hangfire's <c>Enqueue&lt;T&gt;</c> extension resolves to a
/// <see cref="IBackgroundJobClient.Create"/> with an <see cref="EnqueuedState"/>, so the seam to pin is: one
/// job created per call, in the enqueued state, and the id returned verbatim.
/// </summary>
public sealed class HangfireOperationSchedulerTests
{
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly HangfireOperationScheduler _scheduler;

    public HangfireOperationSchedulerTests()
    {
        _jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("job-id");
        _scheduler = new HangfireOperationScheduler(_jobs);
    }

    [Fact]
    public void EnqueueReindex_enqueues_the_rebuild_trigger_and_returns_its_id()
    {
        _scheduler.EnqueueReindex().ShouldBe("job-id");

        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Type == typeof(IndexRebuildTrigger)),
            Arg.Is<IState>(s => s is EnqueuedState));
    }

    [Fact]
    public void EnqueueReprocess_enqueues_the_reprocess_trigger_and_returns_its_id()
    {
        _scheduler.EnqueueReprocess(new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero)).ShouldBe("job-id");

        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Type == typeof(ReprocessTrigger)),
            Arg.Is<IState>(s => s is EnqueuedState));
    }

    [Fact]
    public void EnqueueDailyRun_enqueues_the_daily_run_trigger_and_returns_its_id()
    {
        _scheduler.EnqueueDailyRun().ShouldBe("job-id");

        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Type == typeof(DailyRunTrigger)),
            Arg.Is<IState>(s => s is EnqueuedState));
    }

    [Fact]
    public void EnqueueDigestDelivery_enqueues_the_delivery_trigger_and_returns_its_id()
    {
        _scheduler.EnqueueDigestDelivery().ShouldBe("job-id");

        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Type == typeof(DigestDeliveryTrigger)),
            Arg.Is<IState>(s => s is EnqueuedState));
    }
}
