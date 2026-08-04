using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Lifecycle;

/// <summary>
/// Closes a job whose apply destination the digest assembler confirmed unreachable (F5 SAD §11 D3, AC-11).
/// It is the seam that keeps closure where it belongs: the read-path assembler never mutates a
/// <c>Job</c> — it publishes <see cref="ApplyDestinationUnreachable"/> — and this handler, in the lifecycle
/// layer that F2 owns, performs the actual transition and emits the one <see cref="JobClosed"/>. The reason
/// on that closure is distinct from the liveness sweep's (<see cref="JobLifecycleHandler.StaleAcrossAllSources"/>),
/// so a job closed because its link died is distinguishable from one closed because it fell off every board.
///
/// <para>Idempotent on the job: <c>ClosedAt</c> is the fixed instant carried on the event
/// (<see cref="ApplyDestinationUnreachable.ConfirmedAt"/>), not "now", so a redelivered event closes at the
/// same instant and the <c>(JobId, ClosedAt)</c> key collapses the duplicate <see cref="JobClosed"/> in the
/// inbox (invariant 8). A job already closed, quarantined or superseded is left as it is: closing an
/// already-closed job is a no-op that publishes nothing again, and a quarantined job refuses closure exactly
/// as it does under the sweep — a dead apply link never overrides a human's quarantine.</para>
/// </summary>
public sealed class UnreachableApplyLinkHandler(
    IJobRepository jobs,
    ILogger<UnreachableApplyLinkHandler> logger)
{
    private readonly IJobRepository _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    private readonly ILogger<UnreachableApplyLinkHandler> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>The reason recorded on a closure driven by a confirmed-unreachable apply link (AC-11).</summary>
    public const string ApplyLinkUnreachable = "ApplyLinkUnreachable";

    public async Task Handle(ApplyDestinationUnreachable message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var job = await _jobs.FindAsync(message.JobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            _logger.LogWarning(
                "ApplyDestinationUnreachable for unknown Job {JobId}; nothing to close.", message.JobId);
            return;
        }

        // ClosedAt is the confirmed instant carried on the event, so a redelivery closes at the same instant
        // and the (JobId, ClosedAt) key deduplicates JobClosed downstream (invariant 8).
        var result = job.Close(message.ConfirmedAt);
        if (result.IsFailure)
        {
            // Quarantined or superseded — a dead link never overrides a human's hold; recorded, not thrown.
            _logger.LogInformation(
                "Job {JobId} was not closed for an unreachable apply link: {Reason}.",
                message.JobId, result.Error.Code);
            return;
        }

        await _jobs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new JobClosed(
            job.Id, message.ConfirmedAt, ApplyLinkUnreachable, message.OccurredAt)).ConfigureAwait(false);

        _logger.LogInformation("Closed Job {JobId} because its apply destination was confirmed unreachable.", job.Id);
    }
}
