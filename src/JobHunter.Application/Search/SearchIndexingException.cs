using JobHunter.Domain.Common;

namespace JobHunter.Application.Search;

/// <summary>
/// Thrown by <see cref="SearchIndexingHandler"/> when the search index reports a failure
/// (<see cref="ISearchIndex"/> returned a failed <see cref="Result{T}"/>). An unreachable index is an
/// infrastructure fault, not a business outcome, so it is raised as an exception here on purpose: it is
/// what drives Wolverine's retry-then-dead-letter on the indexer's own queue (SAD §6.1). Because indexing
/// is best-effort and isolated, that dead-letter never reaches another stage — discovery, matching,
/// ranking and the 07:00 digest are all unaffected (QG-3). The read path (<see cref="ISearchQuery"/>)
/// does the opposite: it returns a failed <see cref="Result{T}"/> and never throws.
/// </summary>
public sealed class SearchIndexingException(Guid jobId, string operation, Error error)
    : Exception($"Indexing {operation} for job {jobId} failed: {error.Code}.")
{
    public Guid JobId { get; } = jobId;

    public string Operation { get; } = operation;

    public string ErrorCode { get; } = error.Code;
}
