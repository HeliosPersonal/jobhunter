namespace JobHunter.Application.Enrichment;

/// <summary>
/// The delivery-deadline arithmetic for the batch poller (F3 SAD §6.3, AC-09), as a pure function of the
/// clock. The daily digest ships at 07:00 Europe/Kyiv, so a batch still unfinished at 06:45 local ships
/// partial and carries the rest over — 07:00 is never delayed. Kept separate from the poller so the
/// "is the deadline past" decision is a unit-testable value, not entangled with the provider call.
/// </summary>
public static class PollDeadline
{
    /// <summary>The scheduling time zone for the delivery deadline — 06:45 stays 06:45 across DST.</summary>
    public static readonly TimeZoneInfo Kyiv = ResolveKyiv();

    /// <summary>
    /// The first instant whose local time-of-day is <paramref name="localTimeOfDay"/> in
    /// <paramref name="timeZone"/> and which falls at or after <paramref name="reference"/>. A batch
    /// submitted at ~02:00 local yields the same day's deadline; one submitted after the deadline yields
    /// the next day's, so the arithmetic is total rather than assuming a submission time.
    /// </summary>
    public static DateTimeOffset NextDeadlineAfter(
        DateTimeOffset reference,
        TimeSpan localTimeOfDay,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var localReference = TimeZoneInfo.ConvertTime(reference, timeZone);
        var candidateLocal = new DateTimeOffset(localReference.Date, localReference.Offset) + localTimeOfDay;

        // Recompute the offset at the candidate instant so a DST boundary between reference and deadline is
        // honoured, then roll to the next day if the reference is already past today's deadline.
        candidateLocal = Normalise(candidateLocal, timeZone);
        if (candidateLocal < reference)
        {
            candidateLocal = Normalise(candidateLocal.AddDays(1), timeZone);
        }

        return candidateLocal;
    }

    private static DateTimeOffset Normalise(DateTimeOffset local, TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local.DateTime, offset);
    }

    private static TimeZoneInfo ResolveKyiv()
    {
        foreach (var id in (string[])["Europe/Kyiv", "Europe/Kiev", "FLE Standard Time"])
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows and IANA disagree on the id — try the next alias.
            }
            catch (InvalidTimeZoneException)
            {
                // A corrupt registry entry — try the next alias.
            }
        }

        return TimeZoneInfo.Utc;
    }
}
