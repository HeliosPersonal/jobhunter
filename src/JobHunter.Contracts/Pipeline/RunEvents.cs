namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// A daily Run was created and its scope selected (event-catalog §3, F3 SAD §6.1). Published once when
/// the 02:00 schedule opens the day's work, carrying the discovery window and the job count the
/// enrichment stage will assess. Consumed by observability (the run-state gauge, the digest footer);
/// idempotency key is the <see cref="RunId"/> — a resumed start that re-enters a Created Run re-emits
/// the same key.
/// </summary>
public sealed record RunStarted(
    Guid RunId,
    DateTimeOffset CutoffFrom,
    DateTimeOffset CutoffTo,
    int JobsInScope,
    decimal CeilingUsd,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// An enrichment batch was submitted to the provider and its id persisted (event-catalog §3, F3 SAD
/// §6.2). Published in the same transaction that records the batch row, <em>after</em> the estimated
/// ledger entry (AC-04). Consumed by observability; idempotency key is the
/// <see cref="ProviderBatchId"/> — the unique <c>(run_id, stage, tier)</c> index makes a second
/// submission impossible, so the key never collides across distinct batches.
/// </summary>
public sealed record EnrichmentBatchSubmitted(
    Guid RunId,
    Guid BatchId,
    string ProviderBatchId,
    string PromptVersion,
    int ItemCount,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// The enrichment stage finished — every result was parsed-or-recorded and the Run advanced to
/// <c>Matching</c> (event-catalog §3, F3 SAD §6.2). The one event F3 hands to F4; a zero-scope Run emits
/// it with <see cref="EnrichedCount"/> zero so a digest still ships (brief §9). Idempotency key is the
/// <see cref="RunId"/>: reprocessing the same results converges on the same completion.
/// </summary>
public sealed record EnrichmentCompleted(
    Guid RunId,
    int EnrichedCount,
    int FailedCount,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A matching batch was submitted to the provider and its id persisted (event-catalog §3, F4 SAD §6.1).
/// Published in the same transaction that records the batch row, <em>after</em> the estimated ledger entry
/// (invariant 6). The deep-tier analogue of <see cref="EnrichmentBatchSubmitted"/>. Consumed by the batch
/// poller; idempotency key is the <c>(RunId, Matching, Deep)</c> batch key — the unique index makes a
/// second matching submission for a Run impossible, so the key never collides across distinct batches.
/// </summary>
public sealed record MatchingBatchSubmitted(
    Guid RunId,
    Guid BatchId,
    string ProviderBatchId,
    string PromptVersion,
    int ItemCount,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// The matching stage finished — every result was parsed-or-recorded and the Run is ready to advance to
/// <c>Ranking</c> (event-catalog §3, F4 SAD §6.1). The deep-tier analogue of
/// <see cref="EnrichmentCompleted"/>; a Run with no active CV, no active Profile or an empty scope emits it
/// with zero counts so a reduced digest still ships (brief §9). Consumed by the ranking stage; idempotency
/// key is the <see cref="RunId"/> — reprocessing the same results converges on the same completion.
/// </summary>
public sealed record MatchingCompleted(
    Guid RunId,
    int Succeeded,
    int Failed,
    decimal CostUsd,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// The ranking stage finished — every matched job has exactly one score for the Run and the Run advanced to
/// <c>Researching</c> (event-catalog §3, F4 SAD §6.2). Consumed by F5 reporting, F8 research and F9 indexing.
/// Carries the counts the digest footer needs — how many were ranked and how many suppressed — and the top
/// job ids in digest order, so a downstream consumer need not re-query to know the shape of the day. A Run
/// with nothing to rank (no active Profile, empty scope, or every job suppressed) emits it with a zero
/// <see cref="RankedCount"/> and an empty <see cref="TopJobIds"/> so a reduced digest still ships rather than
/// silence (brief §9, invariant 11). Idempotency key is the <see cref="RunId"/> — re-ranking a Run converges
/// on the same scores and the same completion.
/// </summary>
public sealed record RankingCompleted(
    Guid RunId,
    int RankedCount,
    int SuppressedCount,
    IReadOnlyList<Guid> TopJobIds,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A Run ended in <c>Failed</c> from an unrecoverable fault (event-catalog §3, F3 SAD §6.1). Consumed by
/// the Telegram notifier and the digest footer so a broken night is visible, not silent. Idempotency key
/// is the <see cref="RunId"/> — an already-terminal Run keeps its first reason (Run.Abort is idempotent).
/// </summary>
public sealed record RunFailed(
    Guid RunId,
    string Reason,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>
/// A Run was stopped before spending because the estimate would breach its ceiling (event-catalog §3,
/// F3 SAD §6.1, invariant 6/QG-2). Consumed by F5, which ships a reduced digest <em>synchronously</em>
/// in the same flow — the reporting obligation is discharged here, not by a state transition, because
/// silence is worse than a reduced digest. Idempotency key is the <see cref="RunId"/>.
/// </summary>
public sealed record RunCostAborted(
    Guid RunId,
    decimal EstimatedUsd,
    decimal CeilingUsd,
    decimal SpentUsd,
    string Reason,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
