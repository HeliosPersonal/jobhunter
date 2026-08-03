using JobHunter.Domain.Pipeline;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The write repository for the Run aggregate and the Batch/ledger rows that hang off it (data-model
/// §runs/batches/cost_ledger_entries). It is the seam the orchestrator, submit handler and poller all
/// write through, so their persistence is one tested place rather than scattered EF calls.
///
/// <para>The ledger is <strong>append-only by construction</strong>: this port exposes
/// <see cref="AddLedgerEntry"/> and no update or delete, so a correction can only ever be a compensating
/// entry (ADR-F3-0002). Reads for the resumable set and the active-Run guard are here too so the whole
/// Run write-side has one home.</para>
/// </summary>
public interface IRunRepository
{
    /// <summary>Stages a new Run for insert. The partial unique index rejects a second live Run at commit.</summary>
    void Add(Run run);

    /// <summary>Stages a new Batch for insert. The unique <c>(run_id, stage, tier)</c> index blocks resubmission.</summary>
    void AddBatch(Batch batch);

    /// <summary>Stages a new BatchItem for insert.</summary>
    void AddBatchItem(BatchItem item);

    /// <summary>Appends one cost-ledger entry. The only write path the ledger has — no update, no delete.</summary>
    void AddLedgerEntry(CostLedgerEntry entry);

    /// <summary>The single live Run, if any — the one the active-Run guard and resume path both look for.</summary>
    Task<Run?> FindActiveRunAsync(CancellationToken cancellationToken = default);

    /// <summary>Every non-terminal Run, for the startup resume sweep (QG-1). Served by <c>idx_runs_resumable</c>.</summary>
    Task<IReadOnlyList<Run>> FindResumableRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The latest <c>cutoff_to</c> across every Run, or null when none exists yet. The next Run's
    /// <c>cutoff_from</c> is this value, so a skipped day is caught up rather than lost (data-model §runs);
    /// null bootstraps the very first Run's window from a configured look-back.
    /// </summary>
    Task<DateTimeOffset?> FindMostRecentCutoffAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds a Run by id, or null.</summary>
    Task<Run?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Run's batch for a given <paramref name="stage"/> and <paramref name="tier"/>, or null. The
    /// unique <c>(run_id, stage, tier)</c> index means there is at most one, so a submit handler can tell
    /// "already submitted" from "not yet" and resume without resubmitting (AC-05, QG-1).
    /// </summary>
    Task<Batch?> FindBatchAsync(
        Guid runId,
        BatchStage stage,
        ModelTier tier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a ledger entry of <paramref name="kind"/> already exists for this <paramref name="runId"/>,
    /// <paramref name="stage"/> and <paramref name="tier"/>. The submit handler uses it so a resume after
    /// the estimate was committed but before submission does not write a second estimate — the orphan is
    /// counted once, never doubled (crash-matrix checkpoint 3, ADR-F3-0002).
    /// </summary>
    Task<bool> HasLedgerEntryAsync(
        Guid runId,
        BatchStage stage,
        ModelTier tier,
        LedgerEntryKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <see cref="BatchItem"/> belonging to <paramref name="batchId"/>, tracked for update. The
    /// poller loads them when a batch does not finish before the deadline or the 6 h cap, so it can mark
    /// each as carried-over (<see cref="BatchItemState.ProviderError"/>) and let the next Run re-scope them
    /// via <see cref="FindRetriableJobIdsAsync"/> (AC-08, AC-09).
    /// </summary>
    Task<IReadOnlyList<BatchItem>> FindBatchItemsAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The job ids of items that failed their first attempt and are eligible for their single retry on the
    /// next Run (AC-08): items in <see cref="BatchItemState.ParseFailed"/> or
    /// <see cref="BatchItemState.ProviderError"/> whose retry count is below the ceiling. The next Run's
    /// enrichment scope includes these regardless of their discovery window, so a transient failure is not
    /// a permanent loss.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindRetriableJobIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits the staged changes in one transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
