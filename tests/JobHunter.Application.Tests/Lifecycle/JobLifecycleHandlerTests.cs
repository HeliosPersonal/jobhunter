using JobHunter.Application.Deduplication;
using JobHunter.Application.Lifecycle;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using JobHunter.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Lifecycle;

/// <summary>
/// T08: the daily job-liveness check closes every live job whose every alias has gone stale for two cycles,
/// and publishes exactly one <see cref="JobClosed"/> per closure (AC-06). Closure time is the job's own
/// latest alias sighting — a fixed instant — so a re-run of the same window closes at the same instant and
/// the inbox collapses the duplicate. A job with one fresh alias is not a candidate (the query excludes it),
/// and closure is suspended for a quarantined job both by the query (§D4) and by the aggregate as a second
/// net. The stale-jobs read is substituted, so these are zero-database unit tests.
/// </summary>
public sealed class JobLifecycleHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = Now.AddHours(-12);

    private readonly IStaleJobsQuery _stale = Substitute.For<IStaleJobsQuery>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly FakeClock _clock = new(Now);

    private JobLifecycleHandler CreateHandler() =>
        new(_stale, _jobs, _clock, NullLogger<JobLifecycleHandler>.Instance);

    private Task Handle() =>
        CreateHandler().Handle(new JobLivenessCheckDue(Cutoff), _bus, CancellationToken.None);

    [Fact]
    public async Task It_closes_a_stale_job_at_its_latest_alias_sighting_and_publishes_job_closed()
    {
        var lastSeen = Cutoff.AddHours(-3);
        var job = LiveJob(lastSeen);
        _stale.StaleSinceAsync(Cutoff, Now, Arg.Any<CancellationToken>())
            .Returns([new StaleJob(job.Id, lastSeen)]);
        _jobs.FindAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var closed = new List<JobClosed>();
        await _bus.PublishAsync(Arg.Do<JobClosed>(m => closed.Add(m)));

        await Handle();

        job.Status.ShouldBe(JobStatus.Closed);
        job.ClosedAt.ShouldBe(lastSeen);
        var published = closed.ShouldHaveSingleItem();
        published.JobId.ShouldBe(job.Id);
        published.ClosedAt.ShouldBe(lastSeen);
        published.Reason.ShouldBe(JobLifecycleHandler.StaleAcrossAllSources);
        published.OccurredAt.ShouldBe(Now);
        await _jobs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_check_with_no_stale_jobs_closes_and_publishes_nothing()
    {
        _stale.StaleSinceAsync(Cutoff, Now, Arg.Any<CancellationToken>()).Returns([]);

        await Handle();

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobClosed>());
        await _jobs.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_quarantine_cutoff_passed_to_the_query_is_the_clock_instant()
    {
        _stale.StaleSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await Handle();

        await _stale.Received(1).StaleSinceAsync(Cutoff, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_quarantined_candidate_is_not_closed_even_if_it_slips_through_the_query()
    {
        var lastSeen = Cutoff.AddHours(-3);
        var job = LiveJob(lastSeen);
        job.Quarantine();
        _stale.StaleSinceAsync(Cutoff, Now, Arg.Any<CancellationToken>())
            .Returns([new StaleJob(job.Id, lastSeen)]);
        _jobs.FindAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        await Handle();

        job.Status.ShouldBe(JobStatus.Quarantined);
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobClosed>());
    }

    [Fact]
    public async Task A_job_that_vanished_before_the_check_exits_cleanly()
    {
        var jobId = Guid.CreateVersion7();
        _stale.StaleSinceAsync(Cutoff, Now, Arg.Any<CancellationToken>())
            .Returns([new StaleJob(jobId, Cutoff.AddHours(-3))]);
        _jobs.FindAsync(jobId, Arg.Any<CancellationToken>()).Returns((Job?)null);

        await Should.NotThrowAsync(Handle);

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobClosed>());
    }

    [Fact]
    public async Task Two_runs_for_the_same_window_close_at_the_same_instant()
    {
        var lastSeen = Cutoff.AddHours(-3);
        var jobId = Guid.CreateVersion7();
        _stale.StaleSinceAsync(Cutoff, Now, Arg.Any<CancellationToken>())
            .Returns(_ => [new StaleJob(jobId, lastSeen)]);
        // Each run reads a fresh aggregate, as a durable inbox replay would.
        _jobs.FindAsync(jobId, Arg.Any<CancellationToken>()).Returns(_ => LiveJobWithId(jobId, lastSeen));

        var closed = new List<JobClosed>();
        await _bus.PublishAsync(Arg.Do<JobClosed>(m => closed.Add(m)));

        var handler = CreateHandler();
        await handler.Handle(new JobLivenessCheckDue(Cutoff), _bus, CancellationToken.None);
        await handler.Handle(new JobLivenessCheckDue(Cutoff), _bus, CancellationToken.None);

        // ClosedAt is the job's own last sighting, so both runs carry the same (JobId, ClosedAt) key and the
        // inbox collapses them into a single JobClosed per closed job.
        closed.Count.ShouldBe(2);
        closed[0].JobId.ShouldBe(closed[1].JobId);
        closed[0].ClosedAt.ShouldBe(closed[1].ClosedAt);
    }

    private static Job LiveJob(DateTimeOffset lastSeen) => LiveJobWithId(Guid.CreateVersion7(), lastSeen);

    private static Job LiveJobWithId(Guid id, DateTimeOffset lastSeen)
    {
        var raw = Guid.CreateVersion7();
        var location = LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]);
        var fingerprint = FingerprintCalculator.Compute("acme.com", "senior backend engineer", location);
        var job = new Job(
            id,
            Guid.CreateVersion7(),
            raw,
            fingerprint,
            FingerprintCalculator.Version,
            "Senior Backend Engineer",
            "senior backend engineer",
            "Build things.",
            "https://acme.com/jobs/1",
            location,
            RemotePolicy.Onsite,
            EmploymentType.FullTime,
            PostedAtGranularity.Day,
            lastSeen.AddDays(-5),
            lastSeen);
        job.RegisterAlias(raw, Guid.CreateVersion7(), lastSeen.AddDays(-5), lastSeen);
        return job;
    }
}
