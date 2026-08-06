namespace JobHunter.Domain.Applications;

/// <summary>
/// How long an application may sit in a status before a reminder is due (F6 SAD §8). The thresholds are
/// <b>configuration</b>, not hard-coded durations (T01 AC): the sweep and the aggregate both resolve
/// through this policy, so changing a threshold takes effect without a code change.
///
/// <para>Only the statuses with something to chase carry a threshold — <see cref="ApplicationStatus.Applied"/>
/// (10 d), <see cref="ApplicationStatus.Interview"/> (7 d) and <see cref="ApplicationStatus.Saved"/> (5 d)
/// by default. A terminal or not-yet-acted-on status has none, so no reminder is ever scheduled for it.</para>
/// </summary>
public sealed class ReminderPolicy
{
    private readonly Dictionary<ApplicationStatus, TimeSpan> _thresholds;

    public ReminderPolicy(IReadOnlyDictionary<ApplicationStatus, TimeSpan> thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        foreach (var (status, threshold) in thresholds)
        {
            if (threshold <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    $"The reminder threshold for {status} must be strictly positive.",
                    nameof(thresholds));
            }
        }

        _thresholds = new Dictionary<ApplicationStatus, TimeSpan>(thresholds);
    }

    /// <summary>The SAD §8 defaults: Applied 10 days, Interview 7 days, Saved 5 days.</summary>
    public static ReminderPolicy Default { get; } = new(new Dictionary<ApplicationStatus, TimeSpan>
    {
        [ApplicationStatus.Applied] = TimeSpan.FromDays(10),
        [ApplicationStatus.Interview] = TimeSpan.FromDays(7),
        [ApplicationStatus.Saved] = TimeSpan.FromDays(5),
    });

    /// <summary>The threshold for <paramref name="status"/>, or <c>null</c> when the status has nothing to chase.</summary>
    public TimeSpan? ThresholdFor(ApplicationStatus status) =>
        _thresholds.TryGetValue(status, out var threshold) ? threshold : null;
}
