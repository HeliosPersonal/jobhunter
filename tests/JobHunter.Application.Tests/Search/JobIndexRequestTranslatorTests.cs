using JobHunter.Application.Search;
using JobHunter.Contracts.Pipeline;
using Shouldly;
using Xunit;

namespace JobHunter.Application.Tests.Search;

/// <summary>
/// The translator maps job-lifecycle events to the one <see cref="JobIndexRequested"/> the indexer
/// consumes (F9-T02). It is a pure map: a discovery becomes an upsert, a closure becomes a delete, and
/// <see cref="JobIndexRequested.OccurredAt"/> is carried through unchanged so a replayed lifecycle event
/// produces a byte-identical request the inbox and the id-keyed upsert collapse (invariant 8).
/// </summary>
public sealed class JobIndexRequestTranslatorTests
{
    private static readonly DateTimeOffset Occurred = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_discovered_job_becomes_an_upsert_carrying_the_source_instant()
    {
        var jobId = Guid.CreateVersion7();
        var request = JobIndexRequestTranslator.Handle(
            new JobDiscovered(jobId, Guid.CreateVersion7(), "Engineer", Occurred, Occurred));

        request.JobId.ShouldBe(jobId);
        request.Operation.ShouldBe(JobIndexRequested.Upsert);
        request.OccurredAt.ShouldBe(Occurred);
    }

    [Fact]
    public void A_closed_job_becomes_a_delete_carrying_the_source_instant()
    {
        var jobId = Guid.CreateVersion7();
        var request = JobIndexRequestTranslator.Handle(new JobClosed(jobId, Occurred, "GoneFromBoard", Occurred));

        request.JobId.ShouldBe(jobId);
        request.Operation.ShouldBe(JobIndexRequested.Delete);
        request.OccurredAt.ShouldBe(Occurred);
    }

    [Fact]
    public void Translating_the_same_discovery_twice_is_byte_identical()
    {
        var discovered = new JobDiscovered(Guid.CreateVersion7(), Guid.CreateVersion7(), "Engineer", Occurred, Occurred);

        JobIndexRequestTranslator.Handle(discovered).ShouldBe(JobIndexRequestTranslator.Handle(discovered));
    }

    [Fact]
    public void A_null_event_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => JobIndexRequestTranslator.Handle((JobDiscovered)null!));
        Should.Throw<ArgumentNullException>(() => JobIndexRequestTranslator.Handle((JobClosed)null!));
    }
}
