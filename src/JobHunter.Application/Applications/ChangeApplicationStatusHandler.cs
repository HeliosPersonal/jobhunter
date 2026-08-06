using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Applications;

/// <summary>
/// Moves a tracked job's application to a requested status (F6 T09) — the shared write path behind both the
/// API <c>POST …/status</c> and the Telegram <c>/pipeline</c> status callbacks. Unlike
/// <see cref="OwnerActionHandler"/>, which consumes a Wolverine event on the pipeline host and publishes
/// <see cref="Contracts.Pipeline.ApplicationStatusChanged"/>, this one is invoked directly by a request-driven
/// host that has no message bus (the F4 <c>ReMatchScheduler</c> precedent), so it returns a value-typed
/// <see cref="ChangeApplicationStatusOutcome"/> the caller renders rather than emitting an event.
///
/// <para>It loads the application by job (<see cref="IApplicationRepository.FindByJobAsync"/>, QG-1). A status
/// change annotates an existing application; it does not lazily create one (that is the digest-action path's
/// job), so an untracked job is <see cref="ChangeApplicationStatusResult.ApplicationNotFound"/>, not a new
/// <c>New</c> row. It evaluates the transition through <see cref="Application.ChangeStatus"/>: a refusal
/// returns the remedy as a value (AC-10, coding-standards §4), not an exception, and nothing is written. The
/// <see cref="TransitionSource"/> the caller passes is recorded verbatim, which is what makes an API change and
/// a Telegram change distinguishable in the history (done-when 4).</para>
///
/// <para>When the target is a terminal outcome, <see cref="OutcomeSignalPublisher"/> stages the weighted
/// <c>signals</c> row into the same unit of work before the one <see cref="IApplicationRepository.SaveChangesAsync"/>,
/// so an API-driven Interview is F7 evidence exactly as a Telegram one is (T08, AC-08) and the signal and the
/// transition commit together or not at all.</para>
/// </summary>
public sealed class ChangeApplicationStatusHandler(
    IApplicationRepository applications,
    OutcomeSignalPublisher outcomeSignals,
    ReminderPolicy reminderPolicy,
    ILogger<ChangeApplicationStatusHandler> logger)
{
    private readonly IApplicationRepository _applications = applications ?? throw new ArgumentNullException(nameof(applications));
    private readonly OutcomeSignalPublisher _outcomeSignals = outcomeSignals ?? throw new ArgumentNullException(nameof(outcomeSignals));
    private readonly ReminderPolicy _reminderPolicy = reminderPolicy ?? throw new ArgumentNullException(nameof(reminderPolicy));
    private readonly ILogger<ChangeApplicationStatusHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<ChangeApplicationStatusOutcome> Handle(
        ChangeApplicationStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var application = await _applications.FindByJobAsync(command.JobId, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            // A status change annotates a tracked application; it does not create one (unlike an owner action).
            _logger.LogInformation(
                "Status change to {ToStatus} for job {JobId} refused: no application tracks it yet.",
                command.ToStatus, command.JobId);
            return ChangeApplicationStatusOutcome.NotFound(command.ToStatus);
        }

        var from = application.Status;
        var result = application.ChangeStatus(
            command.ToStatus, command.Source, command.OccurredAt, _reminderPolicy, command.Detail);
        if (result.IsFailure)
        {
            // AC-10: the refusal carries the remedy — the rule and the fix — as a value, not an exception.
            // Nothing changed on the aggregate, so nothing is persisted.
            _logger.LogInformation(
                "Status change {FromStatus} -> {ToStatus} for job {JobId} refused: {Reason}.",
                from, command.ToStatus, command.JobId, result.Error.Code);
            return ChangeApplicationStatusOutcome.NotPermitted(from, command.ToStatus, result.Error.Message);
        }

        // T08 / AC-08: stage the weighted outcome signal into the same unit of work as the transition, so the
        // single SaveChanges commits both (or neither). A non-outcome target stages nothing.
        await _outcomeSignals.StageAsync(
            application.JobId, application.Id, command.ToStatus, command.OccurredAt, cancellationToken)
            .ConfigureAwait(false);

        await _applications.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Status change moved application {ApplicationId} for job {JobId} from {FromStatus} to {ToStatus} via {Source}.",
            application.Id, command.JobId, from, command.ToStatus, command.Source);

        return ChangeApplicationStatusOutcome.Changed(application.Id, from, command.ToStatus);
    }
}
