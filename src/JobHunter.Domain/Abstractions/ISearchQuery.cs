using JobHunter.Domain.Common;
using JobHunter.Domain.Search;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The read port over the search index (SAD §5). Provider-agnostic — the API and the Telegram
/// <c>/search</c> command both depend on this one port and only the renderer differs, so there is no
/// second search path (T09, decision O12). Returns a <see cref="Result{T}"/>: an unreachable index is a
/// clear failure, never a partial result presented as complete and never an exception thrown into the
/// caller (AC-09, QG-3).
/// </summary>
public interface ISearchQuery
{
    Task<Result<SearchResults>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
