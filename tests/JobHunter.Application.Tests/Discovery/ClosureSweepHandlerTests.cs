using JobHunter.Application.Discovery;
using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Postings;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;

namespace JobHunter.Application.Tests.Discovery;

/// <summary>
/// T13: the closure sweep publishes exactly one <see cref="JobClosed"/> per posting gone from its board — a
/// raw posting whose <c>last_seen_at</c> did not advance this cycle. The reason is <c>GoneFromBoard</c> and
/// <c>ClosedAt</c> is the posting's own <c>last_seen_at</c>, a fixed instant. That fixed key is what makes
/// the sweep idempotent: running it twice for the same window produces the same <c>(JobId, ClosedAt)</c> keys
/// and the inbox collapses the duplicate. The candidate read is substituted, so these are zero-database
/// unit tests.
/// </summary>
public sealed class ClosureSweepHandlerTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 8, 3, 6, 0, 0, TimeSpan.Zero);

    private readonly IClosureSweepQuery _closed = Substitute.For<IClosureSweepQuery>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private ClosureSweepHandler CreateHandler() =>
        new(_closed, NullLogger<ClosureSweepHandler>.Instance);

    [Fact]
    public async Task It_publishes_one_job_closed_per_gone_posting()
    {
        var a = new ClosedPosting(Guid.CreateVersion7(), WindowStart.AddHours(-8));
        var b = new ClosedPosting(Guid.CreateVersion7(), WindowStart.AddHours(-9));
        _closed.ClosedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([a, b]);

        var published = new List<JobClosed>();
        await _bus.PublishAsync(Arg.Do<JobClosed>(m => published.Add(m)));

        await CreateHandler().Handle(new ClosureSweepDue(WindowStart), _bus, CancellationToken.None);

        published.Count.ShouldBe(2);
        published.Select(m => m.JobId).ShouldBe([a.RawPostingId, b.RawPostingId]);
        published.ShouldAllBe(m => m.Reason == ClosureSweepHandler.GoneFromBoard);
        published[0].ClosedAt.ShouldBe(a.LastSeenAt);
    }

    [Fact]
    public async Task The_cutoff_passed_to_the_query_is_the_window_start()
    {
        _closed.ClosedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);

        await CreateHandler().Handle(new ClosureSweepDue(WindowStart), _bus, CancellationToken.None);

        await _closed.Received(1).ClosedSinceAsync(WindowStart, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_sweep_with_no_gone_postings_publishes_nothing()
    {
        _closed.ClosedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);

        await CreateHandler().Handle(new ClosureSweepDue(WindowStart), _bus, CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(Arg.Any<JobClosed>());
    }

    [Fact]
    public async Task Two_runs_for_the_same_window_produce_the_same_closure_keys()
    {
        var posting = new ClosedPosting(Guid.CreateVersion7(), WindowStart.AddHours(-8));
        _closed.ClosedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([posting]);

        var published = new List<JobClosed>();
        await _bus.PublishAsync(Arg.Do<JobClosed>(m => published.Add(m)));

        var handler = CreateHandler();
        var due = new ClosureSweepDue(WindowStart);
        await handler.Handle(due, _bus, CancellationToken.None);
        await handler.Handle(due, _bus, CancellationToken.None);

        // ClosedAt is the posting's own last_seen_at, so both runs carry the same (JobId, ClosedAt) key and
        // the inbox collapses them into a single JobClosed per closed posting.
        published.Count.ShouldBe(2);
        published[0].JobId.ShouldBe(published[1].JobId);
        published[0].ClosedAt.ShouldBe(published[1].ClosedAt);
    }
}
