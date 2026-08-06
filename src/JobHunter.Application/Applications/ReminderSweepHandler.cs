using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Applications;
using Microsoft.Extensions.Logging;
using Wolverine;
using DeliveryOptions = JobHunter.Application.Delivery.DeliveryOptions;

namespace JobHunter.Application.Applications;

/// <summary>
/// The reminder sweep (F6 SAD §6.2, T06): on the 08:00 tick (<see cref="ReminderSweepDue"/>) it reads the
/// applications whose <c>next_action_at</c> has passed and, for each one not already reminded for its current
/// condition, sends the Owner one nudge and records the reminder. It is the admin counterpart to the 07:00
/// digest — deliberately an hour later, so the morning message stays about opportunities and the "chase this"
/// nudge is its own, separate message (done-when 6). Discovered and constructed by Wolverine like every other
/// pipeline handler.
///
/// <para>Suppression is one reminder per <c>(application, condition)</c> until the condition clears or recurs
/// (QG-3, done-when 1): the due read carries the last condition, so <see cref="DueReminder.IsAlreadyReminded"/>
/// decides the skip without loading the aggregate. Only when a nudge is actually due is the aggregate loaded
/// through <see cref="IApplicationRepository.FindByJobAsync"/> and mutated via
/// <see cref="Application.RecordReminder"/> — which stamps the condition and pushes <c>next_action_at</c>
/// forward by the current threshold — then committed. The repository has no update or delete path (QG-1); the
/// reschedule is a property write on the loaded aggregate, so a changed threshold takes effect on the next
/// sweep with no per-application rescheduling (done-when 4). Sends are ordered send-then-record, so a crash
/// between the two re-nudges once on resume — an at-least-once nudge, never a dropped one.</para>
///
/// <para>The system never applies for the Owner (invariant 7): the nudge is a message with a link to the
/// posting, nothing more. It reads only public job facts carried on the <see cref="DueReminder"/> and renders
/// through <see cref="IReminderRenderer"/> — the CV crosses exactly one boundary, and it is not this one.</para>
/// </summary>
public sealed class ReminderSweepHandler(
    IDueReminderQuery dueReminders,
    IApplicationRepository applications,
    IReminderRenderer renderer,
    INotifier notifier,
    ReminderPolicy reminderPolicy,
    DeliveryOptions delivery,
    ILogger<ReminderSweepHandler> logger)
{
    private readonly IDueReminderQuery _dueReminders = dueReminders ?? throw new ArgumentNullException(nameof(dueReminders));
    private readonly IApplicationRepository _applications = applications ?? throw new ArgumentNullException(nameof(applications));
    private readonly IReminderRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly INotifier _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    private readonly ReminderPolicy _reminderPolicy = reminderPolicy ?? throw new ArgumentNullException(nameof(reminderPolicy));
    private readonly DeliveryOptions _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
    private readonly ILogger<ReminderSweepHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(ReminderSweepDue message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var due = await _dueReminders.DueAsync(message.SweptAt, cancellationToken).ConfigureAwait(false);
        var chatId = _delivery.OwnerChatId;

        var sent = 0;
        var suppressed = 0;

        foreach (var reminder in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reminder.IsAlreadyReminded())
            {
                // The last reminder already fired for this exact condition — silent until it clears or recurs.
                suppressed++;
                continue;
            }

            // Send first, then record: a crash in the window re-nudges once on resume rather than dropping it.
            var rendered = _renderer.Render(reminder);
            await _notifier.SendAsync(chatId, rendered, cancellationToken).ConfigureAwait(false);

            // Mutate through the aggregate's only write path (QG-1): stamp the condition and push next_action_at
            // forward by the current threshold, then commit. Resolving the threshold now — not at schedule time —
            // is what lets a changed configuration take effect on this sweep with no per-application reschedule.
            var application = await _applications.FindByJobAsync(reminder.JobId, cancellationToken).ConfigureAwait(false);
            if (application is null)
            {
                // The due read joined it a moment ago; a null here means it was archived or removed in between.
                _logger.LogWarning(
                    "Reminder due for job {JobId} but no application was found on load; skipping the record.",
                    reminder.JobId);
                continue;
            }

            var condition = application.RecordReminder(message.SweptAt, _reminderPolicy);
            await _applications.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            sent++;

            _logger.LogInformation(
                "Reminded application {ApplicationId} for condition {Condition}; next action at {NextActionAt:o}.",
                application.Id, condition, application.NextActionAt);
        }

        _logger.LogInformation(
            "Reminder sweep at {SweptAt:o} sent {Sent} nudge(s) and suppressed {Suppressed} already-reminded.",
            message.SweptAt, sent, suppressed);
    }
}
