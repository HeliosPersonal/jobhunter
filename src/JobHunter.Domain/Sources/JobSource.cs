using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;

namespace JobHunter.Domain.Sources;

/// <summary>
/// A binding made operational: the concrete endpoint to fetch, its per-host budget and its health
/// (data-model §job_sources). Health is a small state machine — a run of failures quarantines the
/// source (AC-08), a success clears the counter — kept here rather than in a handler so the invariant
/// "quarantine at exactly the second consecutive failure" is unit-testable without a database.
/// </summary>
public sealed class JobSource : Entity
{
    /// <summary>Consecutive failures at which a source is quarantined (AC-08).</summary>
    public const int QuarantineThreshold = 2;

    public JobSource(
        Guid id,
        Guid companyId,
        Guid bindingId,
        string endpointUrl,
        short requestsPerSecond = 1)
        : base(id)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Source company id must not be empty.", nameof(companyId));
        }

        if (bindingId == Guid.Empty)
        {
            throw new ArgumentException("Source binding id must not be empty.", nameof(bindingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);
        ArgumentOutOfRangeException.ThrowIfLessThan(requestsPerSecond, (short)1);

        CompanyId = companyId;
        BindingId = bindingId;
        EndpointUrl = endpointUrl;
        RequestsPerSecond = requestsPerSecond;
    }

    private JobSource() => EndpointUrl = string.Empty;

    public Guid CompanyId { get; private set; }

    public Guid BindingId { get; private set; }

    /// <summary>Derived from kind + token, stored so it is greppable (data-model §job_sources).</summary>
    public string EndpointUrl { get; private set; }

    public short RequestsPerSecond { get; private set; }

    public short ConsecutiveFailures { get; private set; }

    /// <summary>Set at <see cref="QuarantineThreshold"/> consecutive failures; null when healthy.</summary>
    public DateTimeOffset? QuarantinedUntil { get; private set; }

    public DateTimeOffset? LastFetchedAt { get; private set; }

    /// <summary>True while the source is inside its quarantine window at the clock's instant.</summary>
    public bool IsQuarantined(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return QuarantinedUntil is { } until && until > clock.UtcNow;
    }

    /// <summary>
    /// Records a successful fetch: clears the failure counter and any quarantine, and stamps
    /// <see cref="LastFetchedAt"/>. Idempotent on the health fields — a second success changes nothing.
    /// </summary>
    public void RecordSuccess(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ConsecutiveFailures = 0;
        QuarantinedUntil = null;
        LastFetchedAt = clock.UtcNow;
    }

    /// <summary>
    /// Re-points the operational source at a new binding after an ATS migration (AC-05): the company's
    /// jobs now live on a different provider, so the endpoint changes and the health state resets — the new
    /// board has not failed. The <see cref="CompanyId"/> is unchanged, which is exactly what keeps every
    /// posting already discovered under the old binding attached to the same company (the key is the
    /// company, not the board). Retiring the old binding and recording the new one is the caller's job.
    /// </summary>
    public void RebindTo(Guid bindingId, string endpointUrl, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (bindingId == Guid.Empty)
        {
            throw new ArgumentException("Source binding id must not be empty.", nameof(bindingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);

        BindingId = bindingId;
        EndpointUrl = endpointUrl;
        ConsecutiveFailures = 0;
        QuarantinedUntil = null;
    }

    /// <summary>
    /// Records a failed fetch. On reaching <see cref="QuarantineThreshold"/> consecutive failures the
    /// source is quarantined until <paramref name="clock"/> + <paramref name="quarantineFor"/> and the
    /// method returns <c>true</c> (the caller then publishes <c>SourceQuarantined</c>). Below the
    /// threshold it returns <c>false</c>.
    /// </summary>
    public bool RecordFailure(IClock clock, TimeSpan quarantineFor)
    {
        ArgumentNullException.ThrowIfNull(clock);

        LastFetchedAt = clock.UtcNow;
        if (ConsecutiveFailures < short.MaxValue)
        {
            ConsecutiveFailures++;
        }

        if (ConsecutiveFailures >= QuarantineThreshold)
        {
            var wasHealthy = QuarantinedUntil is null;
            QuarantinedUntil = clock.UtcNow + quarantineFor;
            return wasHealthy;
        }

        return false;
    }
}
