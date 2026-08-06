using JobHunter.Domain.Applications;

namespace JobHunter.Application.Applications;

/// <summary>
/// The reminder thresholds as configuration (F6 SAD §8, T06 done-when 4): how many days an application may
/// sit in a status before the sweep nudges the Owner. Bound and validated at startup (coding-standards
/// §options) and turned into the <see cref="ReminderPolicy"/> the sweep resolves through, so a threshold
/// change takes effect on the next sweep — the sweep reads the policy each run and pushes <c>next_action_at</c>
/// forward by the current value, with no per-application rescheduling. The defaults are the SAD §8 durations,
/// so an unconfigured deployment behaves exactly as the hard-coded <see cref="ReminderPolicy.Default"/> did.
///
/// <para>Only the statuses with something to chase carry a threshold — Applied, Interview and Saved. A
/// terminal or not-yet-acted-on status has none, so no reminder is ever scheduled for it.</para>
/// </summary>
public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    /// <summary>Days an <see cref="ApplicationStatus.Applied"/> application waits before a follow-up nudge.</summary>
    public int AppliedDays { get; init; } = 10;

    /// <summary>Days an <see cref="ApplicationStatus.Interview"/> application waits before a chase nudge.</summary>
    public int InterviewDays { get; init; } = 7;

    /// <summary>Days a <see cref="ApplicationStatus.Saved"/> application waits before an apply-or-drop nudge.</summary>
    public int SavedDays { get; init; } = 5;

    /// <summary>Builds the <see cref="ReminderPolicy"/> the sweep resolves thresholds through.</summary>
    public ReminderPolicy ToPolicy() => new(new Dictionary<ApplicationStatus, TimeSpan>
    {
        [ApplicationStatus.Applied] = TimeSpan.FromDays(AppliedDays),
        [ApplicationStatus.Interview] = TimeSpan.FromDays(InterviewDays),
        [ApplicationStatus.Saved] = TimeSpan.FromDays(SavedDays),
    });
}
