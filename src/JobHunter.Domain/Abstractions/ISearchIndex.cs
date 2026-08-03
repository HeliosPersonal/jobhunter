using JobHunter.Domain.Common;
using JobHunter.Domain.Search;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write port over the search index (SAD §5). Provider-agnostic — no Typesense type appears in
/// Domain (T01) — so the adapter in <c>JobHunter.Search</c> is swappable and the indexing handler depends
/// only on this. Every method returns a <see cref="Result{T}"/>: indexing is best-effort and an
/// unavailable index is an expected business outcome the caller retries or dead-letters, never a fault
/// thrown into the pipeline (QG-3, coding-standards §1). The document id is the job id, so an upsert is
/// idempotent and two racing indexers on one job produce one document.
/// </summary>
public interface ISearchIndex
{
    /// <summary>Ensures the collection exists with the current schema. Idempotent — a present collection is a no-op.</summary>
    Task<Result<bool>> EnsureCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts one document, keyed by its id. Last write wins.</summary>
    Task<Result<bool>> UpsertAsync(JobDocument document, CancellationToken cancellationToken = default);

    /// <summary>Upserts a batch of documents in one round trip, for reconcile and rebuild.</summary>
    Task<Result<int>> UpsertManyAsync(IReadOnlyList<JobDocument> documents, CancellationToken cancellationToken = default);

    /// <summary>Deletes the document with <paramref name="jobId"/>. Deleting an absent document is a success.</summary>
    Task<Result<bool>> DeleteAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>The number of documents currently in the collection, for reconcile drift detection.</summary>
    Task<Result<long>> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the collection entirely and recreates it empty, for a full rebuild (QG-1).</summary>
    Task<Result<bool>> DropAndRecreateAsync(CancellationToken cancellationToken = default);
}
