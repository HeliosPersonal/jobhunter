namespace JobHunter.Application.Enrichment;

/// <summary>
/// Tunables for the batch poller (F3 SAD §6.2/§6.3, T11). Bound and validated at startup
/// (coding-standards §options). Two independent give-up thresholds: the daily delivery
/// <see cref="DeliveryDeadlineLocalTime"/> at which an unfinished batch ships partial so 07:00 is never
/// delayed (AC-09), and the absolute <see cref="MaxPollDuration"/> cap that stops a batch stuck for hours.
/// </summary>
public sealed class PollOptions
{
    public const string SectionName = "Poll";

    /// <summary>
    /// The wall-clock cap on a single batch's polling: once this much has elapsed since submission with
    /// the batch still unfinished, it is marked <c>Failed</c> and its items carry to the next Run
    /// (test-plan §edge cases). The hard safety net that does not depend on the daily deadline.
    /// </summary>
    public TimeSpan MaxPollDuration { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// The daily delivery deadline as a local time-of-day in <see cref="TimeZone"/> (07:00 digest, so the
    /// poll cut is 06:45). A batch still unfinished at this instant ships partial and carries the rest
    /// over so 07:00 is never delayed (AC-09). <see langword="null"/> disables the daily deadline, leaving
    /// only <see cref="MaxPollDuration"/> — the same machinery is reused by F4/F5/F8, which may not want a
    /// delivery-SLA overlay.
    /// </summary>
    public TimeSpan? DeliveryDeadlineLocalTime { get; init; } = new TimeSpan(6, 45, 0);

    /// <summary>The time zone the delivery deadline is read in — Europe/Kyiv, so 06:45 stays 06:45 across DST.</summary>
    public TimeZoneInfo TimeZone { get; init; } = PollDeadline.Kyiv;
}
