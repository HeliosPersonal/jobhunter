using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Application.Search;

/// <summary>The outcome of a rebuild: whether it ran, how many documents it wrote and how long it took.</summary>
public sealed record RebuildReport(bool Skipped, int Documents, TimeSpan Elapsed, bool WithinBudget)
{
    /// <summary>The report for a rebuild that could not take the maintenance gate and skipped.</summary>
    public static RebuildReport SkippedReport { get; } = new(Skipped: true, Documents: 0, Elapsed: TimeSpan.Zero, WithinBudget: true);
}

/// <summary>
/// The one-command full rebuild (F9-T08, AC-10, QG-1, SAD §6.3): drop the collection, recreate it empty,
/// and stream every currently-live job's projection through the <em>same</em> pure
/// <see cref="JobDocumentProjection"/> the indexing handler uses, upserting in batches. Because the
/// document is a pure function of the projection source and the projection source is the single read the
/// indexer and the rebuild both use, a rebuild reconstructs the collection <em>document-by-document</em> —
/// the same jobs come back as byte-identical documents, not merely a matching count. That is the whole of
/// why losing the index is a routine rebuild rather than a data loss.
///
/// <para>A rebuild takes the <see cref="IndexMaintenanceGate"/> for its entire duration, so a nightly
/// reconcile that fires mid-rebuild cannot take the gate and skips (test-plan "reconcile during an active
/// rebuild"). It reports its elapsed wall-clock time against the ten-minute budget so the NFR is
/// observable rather than silently missed. Every index call returns a <see cref="Result{T}"/>: a failed
/// drop or batch fails the rebuild as a value, and no exception is thrown into the caller (QG-3).</para>
/// </summary>
public sealed class IndexRebuildService(
    IJobProjectionSource projectionSource,
    ISearchIndex index,
    IndexMaintenanceGate gate,
    IClock clock,
    IOptions<ReconcileOptions> options,
    ILogger<IndexRebuildService> logger)
{
    private readonly IJobProjectionSource _projectionSource =
        projectionSource ?? throw new ArgumentNullException(nameof(projectionSource));

    private readonly ISearchIndex _index = index ?? throw new ArgumentNullException(nameof(index));

    private readonly IndexMaintenanceGate _gate = gate ?? throw new ArgumentNullException(nameof(gate));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ReconcileOptions _options =
        (options ?? throw new ArgumentNullException(nameof(options))).Value;

    private readonly ILogger<IndexRebuildService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Result<RebuildReport>> RebuildAsync(CancellationToken cancellationToken = default)
    {
        using var lease = _gate.TryAcquire();
        if (lease is null)
        {
            _logger.LogWarning("Index rebuild skipped: another maintenance operation holds the gate.");
            return Result<RebuildReport>.Success(RebuildReport.SkippedReport);
        }

        var startedAt = _clock.UtcNow;
        _logger.LogInformation("Starting full index rebuild.");

        var recreated = await _index.DropAndRecreateAsync(cancellationToken).ConfigureAwait(false);
        if (recreated.IsFailure)
        {
            _logger.LogError("Index rebuild failed to recreate the collection: {Error}.", recreated.Error.Code);
            return Result<RebuildReport>.Failure(recreated.Error);
        }

        var written = 0;
        var batch = new List<JobDocument>(_options.BatchSize);

        await foreach (var source in _projectionSource.ProjectLiveAsync(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(JobDocumentProjection.ToDocument(source));
            if (batch.Count < _options.BatchSize)
            {
                continue;
            }

            var flushed = await FlushAsync(batch, cancellationToken).ConfigureAwait(false);
            if (flushed.IsFailure)
            {
                return Result<RebuildReport>.Failure(flushed.Error);
            }

            written += batch.Count;
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            var flushed = await FlushAsync(batch, cancellationToken).ConfigureAwait(false);
            if (flushed.IsFailure)
            {
                return Result<RebuildReport>.Failure(flushed.Error);
            }

            written += batch.Count;
        }

        var elapsed = _clock.UtcNow - startedAt;
        var withinBudget = elapsed <= _options.RebuildBudget;
        if (!withinBudget)
        {
            _logger.LogWarning(
                "Index rebuild wrote {Documents} documents but took {Elapsed}, over the {Budget} budget.",
                written, elapsed, _options.RebuildBudget);
        }
        else
        {
            _logger.LogInformation(
                "Index rebuild complete: {Documents} documents in {Elapsed}.", written, elapsed);
        }

        return Result<RebuildReport>.Success(new RebuildReport(Skipped: false, written, elapsed, withinBudget));
    }

    private async Task<Result<int>> FlushAsync(List<JobDocument> batch, CancellationToken cancellationToken)
    {
        var result = await _index.UpsertManyAsync(batch, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _logger.LogError(
                "Index rebuild failed to upsert a batch of {Count} documents: {Error}.", batch.Count, result.Error.Code);
        }

        return result;
    }
}
