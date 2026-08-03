namespace JobHunter.Contracts.Pipeline;

/// <summary>
/// A single job needs its search-index document rewritten or removed (SAD §6.1, F9-T02). It is the one
/// message <c>SearchIndexingHandler</c> consumes: the lifecycle events that <em>cause</em> a re-index —
/// <see cref="JobDiscovered"/> and <see cref="JobClosed"/> today, ranking and application changes once
/// F4/F6 land — are translated into this by small per-event handlers, so the indexer depends on one
/// message shape rather than on every producer's. The idempotency key is <c>(JobId, Operation)</c>: two
/// races for the same job produce one document because the document id is the job id (SAD §8), and a
/// redelivered request re-projects the same source to the same document. Indexing is best-effort — a
/// failure here retries then dead-letters on the indexer's own queue and never touches another stage
/// (QG-3).
/// </summary>
public sealed record JobIndexRequested(
    Guid JobId,
    string Operation,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    /// <summary>Re-project the job from PostgreSQL and upsert its document.</summary>
    public const string Upsert = "Upsert";

    /// <summary>Remove the job's document from the index (the job closed).</summary>
    public const string Delete = "Delete";
}
