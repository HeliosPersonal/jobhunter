using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Search;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Search;

/// <summary>
/// Writes the search-index projection for one job (SAD §6.1, F9-T02). It consumes exactly one message,
/// <see cref="JobIndexRequested"/>, so it depends on a single shape rather than on every lifecycle
/// producer; the events that cause a re-index are translated into that message upstream.
///
/// <para>The document id is the job id, so an upsert is idempotent with no lookup and two racing indexers
/// on one job converge to one document (SAD §8). A redelivered request re-projects the same PostgreSQL
/// rows through the same pure <see cref="JobDocumentProjection"/> and writes the same bytes, so a replay
/// is a no-op — the whole of the handler's idempotency (invariant 8).</para>
///
/// <para>An <see cref="Upsert"/> for a job that has since vanished degrades to a delete rather than an
/// error, because the projection source returns null for a job that no longer exists. Every index failure
/// is raised as a <see cref="SearchIndexingException"/> so Wolverine retries then dead-letters on the
/// indexer's own queue — indexing is best-effort and contained, and no other stage is affected (QG-3).</para>
/// </summary>
public sealed class SearchIndexingHandler(
    IJobProjectionSource projectionSource,
    ISearchIndex index,
    ILogger<SearchIndexingHandler> logger)
{
    private readonly IJobProjectionSource _projectionSource =
        projectionSource ?? throw new ArgumentNullException(nameof(projectionSource));

    private readonly ISearchIndex _index = index ?? throw new ArgumentNullException(nameof(index));

    private readonly ILogger<SearchIndexingHandler> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(JobIndexRequested message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Operation == JobIndexRequested.Delete)
        {
            await DeleteAsync(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        var source = await _projectionSource.ProjectAsync(message.JobId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            // The job vanished between the request and this handler — treat the upsert as a delete so the
            // index does not keep a document for a job PostgreSQL no longer has (QG-1).
            _logger.LogInformation(
                "Index upsert for job {JobId} found no source row; deleting any stale document instead.",
                message.JobId);
            await DeleteAsync(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        var document = JobDocumentProjection.ToDocument(source);
        var result = await _index.UpsertAsync(document, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw new SearchIndexingException(message.JobId, message.Operation, result.Error);
        }

        _logger.LogInformation("Indexed job {JobId} ({Operation}).", message.JobId, message.Operation);
    }

    private async Task DeleteAsync(JobIndexRequested message, CancellationToken cancellationToken)
    {
        var result = await _index.DeleteAsync(message.JobId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw new SearchIndexingException(message.JobId, JobIndexRequested.Delete, result.Error);
        }

        _logger.LogInformation("Removed job {JobId} from the search index.", message.JobId);
    }
}
