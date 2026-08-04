using JobHunter.Application.Deduplication;
using JobHunter.Application.Lifecycle;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Lifecycle;

/// <summary>
/// T04: the lifecycle seam that closes a job whose apply destination the digest assembler confirmed
/// unreachable (F5 SAD §11 D3, AC-11). The assembler only <em>flags</em> — it publishes
/// <see cref="ApplyDestinationUnreachable"/> — and this handler performs the actual close and emits the one
/// <see cref="JobClosed"/>, so closure stays in the layer that owns it (F2) and never happens from the read
/// path. The properties that carry it: the closure reason is the apply-link one, distinct from the sweep's;
/// <c>ClosedAt</c> is the confirmed instant on the event (not "now"), so a replay closes at the same instant
/// and the inbox collapses the duplicate (invariant 8); a quarantined job refuses closure exactly as under
/// the sweep; and an unknown job exits cleanly. The repository is substituted, so these are zero-database.
/// </summary>
public sealed class UnreachableApplyLinkHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ConfirmedAt = new(2026, 8, 4, 5, 30, 0, TimeSpan.Zero);

    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private UnreachableApplyLinkHandler CreateHandler() =>
        new(_jobs, NullLogger<UnreachableApplyLinkHandler>.Instance);

    private Task Handle(Guid jobId) =>
        CreateHandler().Handle(new ApplyDestinationUnreachable(jobId, ConfirmedAt, Now), _bus, CancellationToken.None);

    [Fact]
    public async Task It_closes_the_job_at_the_confirmed_instant_and_publishes_job_closed()
    {
        var job = LiveJob();
        _jobs.FindAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var closed = new List<JobClosed>();
        await _bus.PublishAsync(Arg.Do<JobClosed>(m => closed.Add(m)));

        await Handle(job.Id);

        job.Status.ShouldBe(JobStatus.Closed);
        job.ClosedAt.ShouldBe(ConfirmedAt);
        var published = closed.ShouldHaveSingleItem();
        published.JobId.ShouldBe(job.Id);
        published.ClosedAt.ShouldBe(ConfirmedAt);
        published.Reason.ShouldBe(UnreachableApplyLinkHandler.ApplyLinkUnreachable);
        published.OccurredAt.ShouldBe(Now);
        await _jobs.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_job_exits_cleanly_without_publishing()
    {
        var jobId = Guid.CreateVersion7();
        _jobs.FindAsync(jobId, Arg.Any<CancellationToken>()).Returns((Job?)null);

        await Should.NotThrowAsync(() => Handle(jobId));

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobClosed>());
        await _jobs.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_quarantined_job_is_not_closed_by_a_dead_link()
    {
        var job = LiveJob();
        job.Quarantine();
        _jobs.FindAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        await Handle(job.Id);

        // A dead apply link never overrides a human's quarantine: no close, no publish, no save.
        job.Status.ShouldBe(JobStatus.Quarantined);
        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobClosed>());
        await _jobs.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_already_closed_job_is_a_no_op_that_still_publishes_the_same_key()
    {
        // A replay reads a fresh aggregate already closed at the confirmed instant; Close is idempotent and
        // keeps that ClosedAt, so the (JobId, ClosedAt) key matches the first pass and the inbox collapses it.
        var job = LiveJob();
        job.Close(ConfirmedAt);
        _jobs.FindAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var closed = new List<JobClosed>();
        await _bus.PublishAsync(Arg.Do<JobClosed>(m => closed.Add(m)));

        await Handle(job.Id);

        job.ClosedAt.ShouldBe(ConfirmedAt);
        closed.ShouldHaveSingleItem().ClosedAt.ShouldBe(ConfirmedAt);
    }

    private static Job LiveJob()
    {
        var raw = Guid.CreateVersion7();
        var location = LocationSet.Of([JobLocation.TryCreate("Germany", city: "Berlin").Value]);
        var fingerprint = FingerprintCalculator.Compute("acme.com", "senior backend engineer", location);
        var job = new Job(
            Guid.CreateVersion7(),
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
            ConfirmedAt.AddDays(-5),
            ConfirmedAt);
        job.RegisterAlias(raw, Guid.CreateVersion7(), ConfirmedAt.AddDays(-5), ConfirmedAt);
        return job;
    }
}
