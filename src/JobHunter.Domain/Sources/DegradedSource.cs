namespace JobHunter.Domain.Sources;

/// <summary>
/// One source whose coverage is currently degraded: it is inside its quarantine window at the time the
/// summary is read (SAD §6.3, AC-09). Flat by design — the digest footer lists which companies are not
/// being fetched and why, so the read carries only the company, its provider, the failure count and when
/// the quarantine lifts. <see cref="AtsKind"/> is the persisted <c>text</c> value.
/// </summary>
public sealed record DegradedSource(
    Guid SourceId,
    Guid CompanyId,
    string CompanyName,
    string AtsKind,
    int ConsecutiveFailures,
    DateTimeOffset QuarantinedUntil);
