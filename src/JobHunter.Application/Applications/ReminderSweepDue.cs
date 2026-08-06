namespace JobHunter.Application.Applications;

/// <summary>
/// The tick that opens a reminder sweep (F6 SAD §6.2, T06). Enqueued by Hangfire at 08:00 Europe/Kyiv — an
/// hour after the 07:00 digest, so the morning message stays about opportunities and the admin nudge is its
/// own, later message (done-when 6). Handled by <see cref="ReminderSweepHandler"/>, which reads the
/// applications whose <c>next_action_at</c> has passed and sends one nudge per un-suppressed condition. It is
/// an internal application message, not a cross-boundary integration event, so it lives in the Application
/// layer rather than in <c>Contracts</c>.
///
/// <para><see cref="SweptAt"/> is the sweep instant, stamped once from <c>IClock</c> when the tick fires and
/// reused: it is both the <c>next_action_at</c> cutoff for the due read and the "now" the aggregate records
/// each reminder at and pushes the next threshold from, so a sweep that runs twice for the same instant reads
/// the same set and records the same reminders.</para>
/// </summary>
public sealed record ReminderSweepDue(DateTimeOffset SweptAt);
