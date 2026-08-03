namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// One source is due for a fetch this cycle (event-catalog §3). Published one-per-source by the
/// discovery cycle so a single provider's failure is a single message's failure (QG-1). Idempotency
/// key <c>(SourceId, WindowStart)</c>: two overlapping cycles for the same window fetch a source once.
/// </summary>
public sealed record SourceFetchRequested(
    Guid SourceId,
    Guid CompanyId,
    string AtsKind,
    DateTimeOffset WindowStart,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A raw posting whose content genuinely changed was ingested (event-catalog §3, AC-02). Published only
/// on a real insert — an unchanged re-fetch bumps <c>last_seen_at</c> and emits nothing (S6). Consumed by
/// F2 normalisation; idempotency key is the <see cref="ContentHash"/>.
/// </summary>
public sealed record RawPostingIngested(
    Guid RawPostingId,
    Guid SourceId,
    Guid CompanyId,
    string ContentHash,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A source crossed the consecutive-failure threshold and was quarantined (event-catalog §3, AC-08).
/// Consumed by the Telegram notifier (once per quarantine event) and the metrics/digest footer (AC-09).
/// Idempotency key <c>(SourceId, QuarantinedAt)</c>.
/// </summary>
public sealed record SourceQuarantined(
    Guid SourceId,
    Guid CompanyId,
    int ConsecutiveFailures,
    int LastStatus,
    DateTimeOffset QuarantinedAt,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A previously live posting is gone from its board (event-catalog §3). The one event F1 emits into the
/// job lifecycle; consumed downstream by <c>SearchIndexer</c> and <c>ApplicationHandler</c> (F1 only
/// produces it). Idempotency key <c>(JobId, ClosedAt)</c>.
/// </summary>
public sealed record JobClosed(
    Guid JobId,
    DateTimeOffset ClosedAt,
    string Reason,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
