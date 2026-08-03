using JobHunter.Application.Common;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Common;
using JobHunter.Domain.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Application.Search;

/// <summary>The outcome of a reconcile: whether it ran, the measured drift and how many documents it re-indexed.</summary>
public sealed record ReconcileReport(bool Skipped, double Drift, bool Drifted, int Reindexed)
{
    /// <summary>The report for a reconcile that could not take the maintenance gate and skipped.</summary>
    public static ReconcileReport SkippedReport { get; } = new(Skipped: true, Drift: 0d, Drifted: false, Reindexed: 0);
}

/// <summary>
/// The nightly reconcile (F9-T08, AC-10, SAD §6.3): compare the authoritative live-job count in PostgreSQL
/// against the document count in the index, and when they diverge by more than the configured fraction,
/// re-index the live set through the same pure projection the indexer and the rebuild use. Every run emits
/// <c>jobhunter.index.drift</c> so drift that does not self-heal after a re-index is visible on a dashboard
/// rather than discovered by a stale search result (data-model §Reconciliation).
///
/// <para>Drift is the absolute divergence over the live count — <c>|live - indexed| / live</c> — so an index
/// that lost documents and one that kept stale documents both register. It shares the
/// <see cref="IndexMaintenanceGate"/> with the rebuild: a reconcile that fires while a rebuild is dropping
/// and recreating the collection cannot take the gate and skips, so it never compares a half-filled index
/// (test-plan "reconcile during an active rebuild"). A failed index count fails the reconcile as a value —
/// no exception reaches the Hangfire job (QG-3).</para>
/// </summary>
public sealed class IndexReconcileService(
    ILiveJobCounter liveJobCounter,
    IJobProjectionSource projectionSource,
    ISearchIndex index,
    IndexMaintenanceGate gate,
    IOptions<ReconcileOptions> options,
    ILogger<IndexReconcileService> logger)
{
    private readonly ILiveJobCounter _liveJobCounter =
        liveJobCounter ?? throw new ArgumentNullException(nameof(liveJobCounter));

    private readonly IJobProjectionSource _projectionSource =
        projectionSource ?? throw new ArgumentNullException(nameof(projectionSource));

    private readonly ISearchIndex _index = index ?? throw new ArgumentNullException(nameof(index));

    private readonly IndexMaintenanceGate _gate = gate ?? throw new ArgumentNullException(nameof(gate));

    private readonly ReconcileOptions _options =
        (options ?? throw new ArgumentNullException(nameof(options))).Value;

    private readonly ILogger<IndexReconcileService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Result<ReconcileReport>> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        using var lease = _gate.TryAcquire();
        if (lease is null)
        {
            _logger.LogInformation("Index reconcile skipped: a rebuild holds the maintenance gate.");
            return Result<ReconcileReport>.Success(ReconcileReport.SkippedReport);
        }

        var liveJobs = await _liveJobCounter.CountLiveAsync(cancellationToken).ConfigureAwait(false);

        var documentCount = await _index.CountAsync(cancellationToken).ConfigureAwait(false);
        if (documentCount.IsFailure)
        {
            _logger.LogError("Index reconcile could not read the document count: {Error}.", documentCount.Error.Code);
            return Result<ReconcileReport>.Failure(documentCount.Error);
        }

        var drift = ComputeDrift(liveJobs, documentCount.Value);
        Telemetry.IndexDrift.Record(drift);

        if (drift <= _options.DriftThreshold)
        {
            _logger.LogInformation(
                "Index reconcile within tolerance: {LiveJobs} live jobs, {Documents} documents, drift {Drift:P2}.",
                liveJobs, documentCount.Value, drift);
            return Result<ReconcileReport>.Success(new ReconcileReport(Skipped: false, drift, Drifted: false, Reindexed: 0));
        }

        _logger.LogWarning(
            "Index reconcile detected drift {Drift:P2} ({LiveJobs} live jobs vs {Documents} documents); re-indexing.",
            drift, liveJobs, documentCount.Value);

        var reindexed = await ReindexLiveAsync(cancellationToken).ConfigureAwait(false);
        if (reindexed.IsFailure)
        {
            return Result<ReconcileReport>.Failure(reindexed.Error);
        }

        return Result<ReconcileReport>.Success(
            new ReconcileReport(Skipped: false, drift, Drifted: true, reindexed.Value));
    }

    private static double ComputeDrift(long liveJobs, long documentCount)
    {
        if (liveJobs == 0)
        {
            // No live jobs: any document is fully divergent, an empty index is in perfect agreement.
            return documentCount == 0 ? 0d : 1d;
        }

        return Math.Abs(liveJobs - documentCount) / (double)liveJobs;
    }

    private async Task<Result<int>> ReindexLiveAsync(CancellationToken cancellationToken)
    {
        var reindexed = 0;
        var batch = new List<JobDocument>(_options.BatchSize);

        await foreach (var source in _projectionSource.ProjectLiveAsync(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(JobDocumentProjection.ToDocument(source));
            if (batch.Count < _options.BatchSize)
            {
                continue;
            }

            var flushed = await _index.UpsertManyAsync(batch, cancellationToken).ConfigureAwait(false);
            if (flushed.IsFailure)
            {
                return Result<int>.Failure(flushed.Error);
            }

            reindexed += batch.Count;
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            var flushed = await _index.UpsertManyAsync(batch, cancellationToken).ConfigureAwait(false);
            if (flushed.IsFailure)
            {
                return Result<int>.Failure(flushed.Error);
            }

            reindexed += batch.Count;
        }

        return Result<int>.Success(reindexed);
    }
}
