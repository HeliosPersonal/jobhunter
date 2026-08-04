using JobHunter.Application.Profiles;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Pipeline;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Profiles;

/// <summary>
/// The re-match scheduler (T09, ADR-F4-0002, AC-08): the re-staling and re-match half of a CV change. It
/// marks matches from older CV versions stale — never deleting — and queues the recent live jobs for
/// cheap-tier re-match against the new version, idempotently per job. These tests hold it to every
/// "Done when" clause without a database: the sweep targets everything but the active version, the window
/// start is derived from the clock, every queued item is cheap-tier, and a job already queued is not
/// queued twice.
/// </summary>
public sealed class ReMatchSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private readonly IMatchRepository _matches = Substitute.For<IMatchRepository>();
    private readonly ILiveJobsQuery _liveJobs = Substitute.For<ILiveJobsQuery>();
    private readonly IReMatchBacklog _queue = Substitute.For<IReMatchBacklog>();
    private readonly SequentialIdGenerator _ids = new();
    private readonly FakeClock _clock = new(Now);

    private ReMatchScheduler NewScheduler(ReMatchOptions? options = null) =>
        new(_matches, _liveJobs, _queue, options ?? new ReMatchOptions(), _ids, _clock,
            NullLogger<ReMatchScheduler>.Instance);

    private static LiveJob Job(Guid id, DateTimeOffset firstSeen) =>
        new(id, Guid.CreateVersion7(), "Staff SRE", "staff", "Remote", "FullTime",
            "https://acme.com/apply", firstSeen, firstSeen);

    private void HasLiveJobs(params LiveJob[] jobs) =>
        _liveJobs.DiscoveredSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(jobs);

    private void EnqueueSucceeds() =>
        _queue.EnqueueAsync(Arg.Any<ReMatchQueueItem>(), Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task It_re_stales_matches_from_every_version_except_the_activated_one()
    {
        HasLiveJobs();
        var active = Guid.CreateVersion7();
        _matches.MarkNotCurrentExceptCvVersionAsync(active, Arg.Any<CancellationToken>()).Returns(4);

        var outcome = await NewScheduler().ScheduleAsync(active);

        outcome.StaledMatches.ShouldBe(4);
        await _matches.Received(1).MarkNotCurrentExceptCvVersionAsync(active, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_queries_live_jobs_from_exactly_one_window_before_now()
    {
        HasLiveJobs();
        var options = new ReMatchOptions { Window = TimeSpan.FromDays(30) };

        await NewScheduler(options).ScheduleAsync(Guid.CreateVersion7());

        await _liveJobs.Received(1)
            .DiscoveredSinceAsync(Now - TimeSpan.FromDays(30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_queues_every_live_job_at_the_cheap_tier_against_the_new_version()
    {
        var active = Guid.CreateVersion7();
        var jobA = Guid.CreateVersion7();
        var jobB = Guid.CreateVersion7();
        HasLiveJobs(Job(jobA, Now.AddDays(-1)), Job(jobB, Now.AddDays(-2)));
        var enqueued = new List<ReMatchQueueItem>();
        _queue.EnqueueAsync(Arg.Do<ReMatchQueueItem>(enqueued.Add), Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await NewScheduler().ScheduleAsync(active);

        outcome.QueuedJobs.ShouldBe(2);
        enqueued.Select(i => i.JobId).ShouldBe(new[] { jobA, jobB });
        enqueued.ShouldAllBe(i => i.Tier == ModelTier.Cheap);
        enqueued.ShouldAllBe(i => i.CvVersionId == active);
    }

    [Fact]
    public async Task A_job_already_queued_is_not_counted_twice()
    {
        var jobA = Guid.CreateVersion7();
        var jobB = Guid.CreateVersion7();
        HasLiveJobs(Job(jobA, Now.AddDays(-1)), Job(jobB, Now.AddDays(-2)));
        // The queue's idempotent upsert reports the first job as already present.
        _queue.EnqueueAsync(Arg.Is<ReMatchQueueItem>(i => i != null && i.JobId == jobA), Arg.Any<CancellationToken>()).Returns(false);
        _queue.EnqueueAsync(Arg.Is<ReMatchQueueItem>(i => i != null && i.JobId == jobB), Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await NewScheduler().ScheduleAsync(Guid.CreateVersion7());

        // Both are attempted, but only the genuinely-new one is counted as queued.
        outcome.QueuedJobs.ShouldBe(1);
        await _queue.Received(2).EnqueueAsync(Arg.Any<ReMatchQueueItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_activated_version_id_is_a_programmer_error()
    {
        await Should.ThrowAsync<ArgumentException>(() => NewScheduler().ScheduleAsync(Guid.Empty));
    }

    [Fact]
    public async Task With_no_recent_live_jobs_it_still_re_stales_and_queues_nothing()
    {
        EnqueueSucceeds();
        HasLiveJobs();
        _matches.MarkNotCurrentExceptCvVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(2);

        var outcome = await NewScheduler().ScheduleAsync(Guid.CreateVersion7());

        outcome.StaledMatches.ShouldBe(2);
        outcome.QueuedJobs.ShouldBe(0);
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }
}
