using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Applications;

/// <summary>
/// Marks a tracked application's posting closed when its job closes (<see cref="JobClosed"/>, F1/F2; SAD §6.3).
/// It loads the application for the job, and — if one exists and has not reached an outcome — records the
/// closure through <c>MarkPostingClosed</c>: <c>posting_closed = true</c> plus a
/// <see cref="Domain.Applications.TransitionSource.System"/> self-transition carrying
/// <c>posting closed</c> as its detail (AC-07). The status is deliberately <b>not</b> changed — a posting
/// closing tells us nothing about the Owner's application, and auto-rejecting would fabricate an outcome and
/// poison F7's preference evidence.
///
/// <para>A closure for a terminal or non-existent application is a no-op, not an error (the outcome is already
/// known, or there is nothing to mark). It publishes nothing: the status did not change, so no
/// <see cref="ApplicationStatusChanged"/> is emitted. Idempotence rests on the durable inbox collapsing a
/// redelivered <c>JobClosed</c> (idempotency key <c>(JobId, ClosedAt)</c>), and — as a second net —
/// <c>MarkPostingClosed</c> itself being a no-op on an already-closed posting, so a redelivered closure
/// changes nothing further and records no second transition. The application and its full history are always
/// retained; nothing is deleted (invariant 8, QG-1).</para>
/// </summary>
public sealed class JobClosureHandler(
    IApplicationRepository applications,
    ILogger<JobClosureHandler> logger)
{
    private readonly IApplicationRepository _applications = applications ?? throw new ArgumentNullException(nameof(applications));
    private readonly ILogger<JobClosureHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(JobClosed message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var application = await _applications.FindByJobAsync(message.JobId, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            // Nothing is tracked for this job — the Owner never acted on the card. Closing is a no-op.
            _logger.LogDebug("Job {JobId} closed with no tracked application; nothing to mark.", message.JobId);
            return;
        }

        if (application.IsTerminal)
        {
            // The outcome is already known (Rejected/Offer/Ignored); a closed posting adds nothing (SAD §6.3).
            _logger.LogDebug(
                "Job {JobId} closed but application {ApplicationId} is terminal ({Status}); nothing to mark.",
                message.JobId, application.Id, application.Status);
            return;
        }

        if (application.PostingClosed)
        {
            // Already marked (a redelivered closure that slipped past the inbox); MarkPostingClosed would be a
            // no-op, but skipping the save keeps this a genuine no-op with no spurious write.
            _logger.LogDebug(
                "Job {JobId} closure for application {ApplicationId} is already recorded; skipping.",
                message.JobId, application.Id);
            return;
        }

        application.MarkPostingClosed(message.ClosedAt);
        await _applications.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Job {JobId} closed; marked application {ApplicationId} posting closed without changing status {Status}.",
            message.JobId, application.Id, application.Status);
    }
}
