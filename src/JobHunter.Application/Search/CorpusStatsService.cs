using JobHunter.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Search;

/// <summary>
/// The corpus snapshot behind <c>GET /api/admin/stats</c> (F9 operational endpoints, runbook R8): the
/// authoritative live-job count in PostgreSQL, the document count in the search index and the drift
/// between them — the same figure the nightly reconcile acts on, so an operator sees what the reconcile
/// will do before it runs. Read-only and provider-agnostic: it composes the <see cref="ILiveJobCounter"/>
/// and <see cref="ISearchIndex"/> ports and no scheduler or HTTP type.
///
/// <para>It never throws for an unreachable index (QG-3): the PostgreSQL count is authoritative and always
/// reported, and a failed index count is surfaced as <see cref="CorpusStats.IndexAvailable"/> <c>false</c>
/// with a null document count and null drift, so the stats endpoint answers even while Typesense is down —
/// which is exactly the condition an operator most wants a number for. The cost trend F3 owns is a
/// deferred, empty slot until F3 merges (the cross-feature decoupling decision), never fabricated.</para>
/// </summary>
public sealed class CorpusStatsService(
    ILiveJobCounter liveJobCounter,
    ISearchIndex index,
    ILogger<CorpusStatsService> logger)
{
    private readonly ILiveJobCounter _liveJobCounter =
        liveJobCounter ?? throw new ArgumentNullException(nameof(liveJobCounter));

    private readonly ISearchIndex _index = index ?? throw new ArgumentNullException(nameof(index));

    private readonly ILogger<CorpusStatsService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<CorpusStats> CollectAsync(CancellationToken cancellationToken = default)
    {
        var liveJobs = await _liveJobCounter.CountLiveAsync(cancellationToken).ConfigureAwait(false);

        var documentCount = await _index.CountAsync(cancellationToken).ConfigureAwait(false);
        if (documentCount.IsFailure)
        {
            // The index is unreachable — report the PostgreSQL truth and say so, never fail the whole
            // snapshot on a dependency the stats are partly meant to diagnose (QG-3).
            _logger.LogWarning(
                "Corpus stats could not read the index document count: {Error}.", documentCount.Error.Code);
            return new CorpusStats(liveJobs, IndexedDocuments: null, Drift: null, IndexAvailable: false);
        }

        var indexed = documentCount.Value;
        var drift = ComputeDrift(liveJobs, indexed);
        return new CorpusStats(liveJobs, indexed, drift, IndexAvailable: true);
    }

    private static double ComputeDrift(long liveJobs, long documentCount)
    {
        if (liveJobs == 0)
        {
            return documentCount == 0 ? 0d : 1d;
        }

        return Math.Abs(liveJobs - documentCount) / (double)liveJobs;
    }
}

/// <summary>
/// A corpus snapshot: the live-job count, the index document count (null when the index is unreachable),
/// the drift between them (null when the index is unreachable) and whether the index answered.
/// </summary>
public sealed record CorpusStats(long LiveJobs, long? IndexedDocuments, double? Drift, bool IndexAvailable);
