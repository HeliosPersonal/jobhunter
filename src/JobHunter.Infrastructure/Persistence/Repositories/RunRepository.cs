using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence.Repositories;

/// <summary>
/// The EF Core write repository for the Run aggregate and its Batch, BatchItem and ledger rows
/// (data-model §runs). Writes go through the tracked context so the partial unique index (one live Run)
/// and the unique <c>(run_id, stage, tier)</c> index (no double submission) are enforced at commit —
/// the repository does not re-check them in code, because the database is the arbiter (SAD S2).
///
/// <para>The ledger is append-only by construction: the port offers only <see cref="AddLedgerEntry"/>,
/// so there is no update or delete path to misuse (ADR-F3-0002).</para>
/// </summary>
public sealed class RunRepository(JobHunterDbContext context) : IRunRepository
{
    public void Add(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        context.Add(run);
    }

    public void AddBatch(Batch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        context.Add(batch);
    }

    public void AddBatchItem(BatchItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        context.Add(item);
    }

    public void AddLedgerEntry(CostLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        context.Add(entry);
    }

    public Task<Run?> FindActiveRunAsync(CancellationToken cancellationToken = default) =>
        NonTerminalRuns().OrderBy(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken);

    public Task<Run?> FindMostRecentRunAsync(CancellationToken cancellationToken = default) =>
        // The day's Run whatever its state — a terminal CostAborted/Failed Run is exactly what the 06:45 and
        // 07:00 ticks must still find to assemble and deliver its degraded digest (ADR-F5-0001).
        context.Set<Run>().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Run>> FindResumableRunsAsync(CancellationToken cancellationToken = default) =>
        await NonTerminalRuns().OrderBy(r => r.StartedAt).ToListAsync(cancellationToken);

    public Task<Run?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Set<Run>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Batch?> FindBatchAsync(
        Guid runId,
        BatchStage stage,
        ModelTier tier,
        CancellationToken cancellationToken = default) =>
        context.Set<Batch>()
            .FirstOrDefaultAsync(
                b => b.RunId == runId && b.Stage == stage && b.Tier == tier,
                cancellationToken);

    public Task<bool> HasLedgerEntryAsync(
        Guid runId,
        BatchStage stage,
        ModelTier tier,
        LedgerEntryKind kind,
        CancellationToken cancellationToken = default) =>
        context.Set<CostLedgerEntry>()
            .AnyAsync(
                e => e.RunId == runId && e.Stage == stage && e.Tier == tier && e.Kind == kind,
                cancellationToken);

    public async Task<IReadOnlyList<BatchItem>> FindBatchItemsAsync(
        Guid batchId,
        CancellationToken cancellationToken = default) =>
        await context.Set<BatchItem>()
            .Where(i => i.BatchId == batchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> FindRetriableJobIdsAsync(CancellationToken cancellationToken = default) =>
        await context.Set<BatchItem>()
            .Where(i =>
                (i.State == BatchItemState.ParseFailed || i.State == BatchItemState.ProviderError) &&
                i.RetryCount < BatchItem.MaxRetries)
            .Select(i => i.JobId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<DateTimeOffset?> FindMostRecentCutoffAsync(CancellationToken cancellationToken = default)
    {
        // The next Run's cutoff_from is the latest cutoff_to across all Runs, so a skipped day is caught
        // up rather than lost (data-model §runs). Max over an empty table is null — the first Run's window
        // is bootstrapped from a configured look-back instead.
        var cutoffs = await context.Set<Run>()
            .OrderByDescending(r => r.CutoffTo)
            .Select(r => (DateTimeOffset?)r.CutoffTo)
            .Take(1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return cutoffs.Count > 0 ? cutoffs[0] : null;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    // A Run is resumable exactly when it is not terminal — the same predicate the partial index uses,
    // so the query is index-covered rather than a sequential scan (T04 query-plan assertion).
    private IQueryable<Run> NonTerminalRuns() =>
        context.Set<Run>().Where(r =>
            r.State != RunState.Delivered &&
            r.State != RunState.Failed &&
            r.State != RunState.CostAborted);
}
