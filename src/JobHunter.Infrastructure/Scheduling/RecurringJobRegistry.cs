namespace JobHunter.Infrastructure.Scheduling;

/// <summary>
/// The seam that lets a later feature register a schedule by adding one line in its own
/// <c>DependencyInjection.cs</c> — no edit to any F0 file (T09 / QG-1). Registrations are collected
/// here and applied once against Hangfire when the Worker starts. Cron expressions are declared in the
/// <c>Europe/Kyiv</c> time zone so 07:00 stays 07:00 across DST.
/// </summary>
public sealed class RecurringJobRegistry
{
    /// <summary>The scheduling time zone for every JobHunter recurring job (SAD §8).</summary>
    public static readonly TimeZoneInfo Kyiv = ResolveKyiv();

    private readonly List<RecurringJobRegistration> _registrations = [];

    public IReadOnlyList<RecurringJobRegistration> Registrations => _registrations;

    /// <summary>
    /// Declares a recurring job. <paramref name="cron"/> is interpreted in <see cref="Kyiv"/>.
    /// </summary>
    public RecurringJobRegistry AddRecurring(string jobId, string cron)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cron);

        if (_registrations.Any(r => string.Equals(r.JobId, jobId, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A recurring job with id '{jobId}' is already registered.", nameof(jobId));
        }

        _registrations.Add(new RecurringJobRegistration(jobId, cron, Kyiv));
        return this;
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
                // Try the next alias — Windows and IANA disagree on the id.
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupt registry entry; try the next alias.
            }
        }

        return TimeZoneInfo.Utc;
    }
}

/// <summary>One declared recurring job: an id, a cron expression and the time zone it is read in.</summary>
public sealed record RecurringJobRegistration(string JobId, string Cron, TimeZoneInfo TimeZone);
