using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Discovery;

/// <summary>
/// The closure sweep (SAD §6.1, T13): after a discovery cycle, the postings that were previously live but no
/// longer appear on their board — a <c>raw_postings.last_seen_at</c> that did not advance this cycle — are
/// the F1 signal that a job is gone. This handler reads those candidates and publishes exactly one
/// <see cref="JobClosed"/> per posting. It is the one event F1 emits into the job lifecycle; downstream
/// <c>SearchIndexer</c> and <c>ApplicationHandler</c> consume it (F1 only produces it).
///
/// Idempotency is structural: each <see cref="JobClosed"/> keys on <c>(JobId, ClosedAt)</c>, and
/// <c>ClosedAt</c> is the posting's own <c>last_seen_at</c> — a fixed instant, not "now". So a sweep that
/// runs twice for the same window reads the same candidates and produces the same keys, and the inbox
/// collapses the duplicate: one <see cref="JobClosed"/> per closed posting (invariant 8). A posting that
/// reappeared before the sweep bumped its <c>last_seen_at</c> past the cutoff and is not a candidate.
/// </summary>
public sealed class ClosureSweepHandler(
    IClosureSweepQuery closedPostings,
    ILogger<ClosureSweepHandler> logger)
{
    private readonly IClosureSweepQuery _closedPostings = closedPostings ?? throw new ArgumentNullException(nameof(closedPostings));
    private readonly ILogger<ClosureSweepHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>The reason recorded on every F1-originated closure (event-catalog §3).</summary>
    public const string GoneFromBoard = "GoneFromBoard";

    public async Task Handle(ClosureSweepDue message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var closed = await _closedPostings.ClosedSinceAsync(message.WindowStart, cancellationToken).ConfigureAwait(false);

        foreach (var posting in closed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ClosedAt is the posting's own last_seen_at, so replaying this sweep yields the same
            // (JobId, ClosedAt) key and the inbox deduplicates it — exactly one JobClosed per closed posting.
            await bus.PublishAsync(new JobClosed(
                posting.RawPostingId, posting.LastSeenAt, GoneFromBoard, posting.LastSeenAt))
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Closure sweep for window {WindowStart:o} closed {Count} posting(s) gone from their board.",
            message.WindowStart, closed.Count);
    }
}
