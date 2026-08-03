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

    /// <summary>Finds a Run by id, or null.</summary>
    Task<Run?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Commits the staged changes in one transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
