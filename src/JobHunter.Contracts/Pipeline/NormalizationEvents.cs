namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// A raw posting was normalised into a candidate canonical job (event-catalog §3). Published by
/// <c>NormalizationHandler</c> and consumed by <c>DeduplicationHandler</c>; the idempotency key is the
/// <see cref="RawPostingId"/>. The <see cref="JobId"/> is a <em>candidate</em> — it becomes the canonical
/// id only if the deduplication insert wins the fingerprint race (SAD §6.1). The <see cref="Fingerprint"/>
/// is the conservative dedup key computed at normalisation (ADR-F2-0001); it is carried so the metric and
/// trace can correlate without re-deriving it.
/// </summary>
public sealed record JobNormalized(
    Guid JobId,
    Guid RawPostingId,
    Guid CompanyId,
    string Fingerprint,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A job passed deduplication and is canonical — a genuinely new opening (event-catalog §3, §4). Published
/// by <c>DeduplicationHandler</c> on a real insert and consumed by the Run orchestrator and search indexer;
/// idempotency key is the <see cref="JobId"/>.
/// </summary>
public sealed record JobDiscovered(
    Guid JobId,
    Guid CompanyId,
    string Title,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A raw posting was found to be the same opening as an existing canonical job and recorded as an alias,
/// not a new job (event-catalog §3). Published by <c>DeduplicationHandler</c> on a fingerprint conflict and
/// consumed by the metrics handler only; idempotency key is <c>(CanonicalJobId, DuplicateRawPostingId)</c>
/// (AUDIT-RESOLUTION-DECISIONS §10).
/// </summary>
public sealed record JobDuplicateDetected(
    Guid CanonicalJobId,
    Guid DuplicateRawPostingId,
    Guid SourceId,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
