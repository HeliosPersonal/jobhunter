using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using Microsoft.Extensions.Logging;
using Wolverine;
using App = JobHunter.Domain.Applications.Application;

namespace JobHunter.Application.Applications;

/// <summary>
/// Turns a digest action (<see cref="OwnerActionRecorded"/>, F5 T10) into a tracked application (SAD §6.1).
/// It loads the application for the job, creates it lazily in <see cref="ApplicationStatus.New"/> on the
/// first action (S2 — a delivered card with no action creates nothing), evaluates the target against
/// <see cref="TransitionRules"/>, applies it as append-only history (QG-1), and publishes
/// <see cref="ApplicationStatusChanged"/> so F7 (signal capture) and F9 (index update) react. The status
/// change, the history row and the outbox message commit together in the one Wolverine EF transaction
/// (AC-03).
///
/// <para>A refused transition changes nothing and publishes nothing (AC-02): <c>ChangeStatus</c> returns a
/// remedy value rather than throwing, so a nonsensical tap (a Save on a Rejected application) is a logged
/// no-op, not an error. <see cref="OwnerActionRecorded.Open"/> is a URL button with no pipeline effect, so
/// it creates and changes nothing.</para>
///
/// <para>Idempotence is the SAD §8 key <c>(application_id, to_status, occurred_at)</c>. Two effects guard it:
/// the durable inbox collapses a redelivered envelope before the handler runs, and — as a second net for a
/// genuinely re-derived duplicate — the handler skips an action whose exact <c>(to, occurred_at)</c>
/// transition already exists, so a double-tap appends no second transition and re-emits nothing. The system
/// never applies for the Owner (invariant 7): <see cref="OwnerActionRecorded.Applied"/> is the Owner
/// recording an outcome, and its only effects are a transition and the status-change event.</para>
/// </summary>
public sealed class OwnerActionHandler(
    IApplicationRepository applications,
    IIdGenerator ids,
    ReminderPolicy reminderPolicy,
    ILogger<OwnerActionHandler> logger)
{
    private readonly IApplicationRepository _applications = applications ?? throw new ArgumentNullException(nameof(applications));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly ReminderPolicy _reminderPolicy = reminderPolicy ?? throw new ArgumentNullException(nameof(reminderPolicy));
    private readonly ILogger<OwnerActionHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(OwnerActionRecorded message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var target = TargetOf(message.Action);
        if (target is null)
        {
            // Open opens the posting directly (URL button) and never advances the pipeline — nothing to track.
            _logger.LogDebug(
                "Owner action {Action} on job {JobId} has no pipeline effect; nothing tracked.",
                message.Action, message.JobId);
            return;
        }

        var application = await _applications.FindByJobAsync(message.JobId, cancellationToken).ConfigureAwait(false);
        var created = application is null;
        if (application is null)
        {
            // S2: the application is created lazily, in New, only now that the first action has arrived.
            application = App.Create(_ids.NewId(), message.JobId, message.OccurredAt, TransitionSource.Telegram);
            _applications.Add(application);
        }

        // Idempotency second net (SAD §8): a redelivered identical action carries the same
        // (to_status, occurred_at); its transition already exists, so append nothing and re-emit nothing.
        if (AlreadyRecorded(application, target.Value, message.OccurredAt))
        {
            _logger.LogInformation(
                "Owner action {Action} on job {JobId} at {OccurredAt:o} is already recorded; skipping.",
                message.Action, message.JobId, message.OccurredAt);
            return;
        }

        var from = application.Status;
        var result = application.ChangeStatus(target.Value, TransitionSource.Telegram, message.OccurredAt, _reminderPolicy);
        if (result.IsFailure)
        {
            // AC-02: a refused transition leaves the status and the history untouched. A lazily-created
            // application only ever transitions from New, whose column permits every non-Offer target, so a
            // refusal here can only be on an existing application — nothing to unwind, nothing to persist.
            _logger.LogInformation(
                "Owner action {Action} on job {JobId} was refused: {Reason}.",
                message.Action, message.JobId, result.Error.Code);
            return;
        }

        await _applications.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await bus.PublishAsync(new ApplicationStatusChanged(
            application.Id,
            application.JobId,
            from.ToString(),
            target.Value.ToString(),
            message.OccurredAt)).ConfigureAwait(false);

        _logger.LogInformation(
            "Owner action {Action} on job {JobId} moved application {ApplicationId} from {FromStatus} to {ToStatus} (created: {Created}).",
            message.Action, message.JobId, application.Id, from, target.Value, created);
    }

    private static bool AlreadyRecorded(App application, ApplicationStatus to, DateTimeOffset occurredAt) =>
        application.Transitions.Any(t => t.To == to && t.OccurredAt == occurredAt);

    private static ApplicationStatus? TargetOf(string action) => action switch
    {
        OwnerActionRecorded.Save => ApplicationStatus.Saved,
        OwnerActionRecorded.Ignore => ApplicationStatus.Ignored,
        OwnerActionRecorded.Applied => ApplicationStatus.Applied,
        // Open is a URL button with no pipeline effect; any unknown action is treated the same, defensively.
        _ => null,
    };
}
