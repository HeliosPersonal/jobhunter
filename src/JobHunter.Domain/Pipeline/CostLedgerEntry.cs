using JobHunter.Domain.Common;

namespace JobHunter.Domain.Pipeline;

/// <summary>
/// One append-only entry in the cost ledger (data-model §cost_ledger_entries, ADR-F3-0002). Two entries
/// are written per batch: the <see cref="LedgerEntryKind.Estimated"/> figure <em>before</em> submission
/// — the ceiling is checked against it, which is what makes the ceiling a precondition rather than an
/// alarm (QG-2) — and the <see cref="LedgerEntryKind.Actual"/> figure on retrieval from the provider's
/// reported usage. The table has no update or delete path; a correction is a compensating entry, so the
/// history of what a Run was believed to cost is never rewritten.
///
/// <para><see cref="BatchId"/> is nullable by design: the <see cref="LedgerEntryKind.Estimated"/> entry is
/// committed <em>before</em> the batch is submitted and its provider id known (AC-04, ADR-F3-0002), so at
/// the instant the ceiling is checked there is no batch row to point at yet. The batch row — carrying the
/// provider id — is recorded only after <see cref="Abstractions.ILlmBatchClient.SubmitAsync"/> returns.
/// The estimate is attributed by <see cref="RunId"/>, <see cref="Stage"/> and <see cref="Tier"/>, which is
/// exactly the ceiling-check key (data-model §cost_ledger_entries), so the missing batch link costs the
/// attribution nothing.</para>
/// </summary>
public sealed class CostLedgerEntry : Entity
{
    public CostLedgerEntry(
        Guid id,
        Guid runId,
        Guid? batchId,
        BatchStage stage,
        ModelTier tier,
        LedgerEntryKind kind,
        decimal costUsd,
        int inputTokens,
        int outputTokens,
        DateTimeOffset recordedAt)
        : base(id)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A ledger entry must belong to a Run.", nameof(runId));
        }

        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("A ledger entry's batch id must be null or a real batch, never empty.", nameof(batchId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(costUsd);
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);

        RunId = runId;
        BatchId = batchId;
        Stage = stage;
        Tier = tier;
        Kind = kind;
        CostUsd = costUsd;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        RecordedAt = recordedAt;
    }

    private CostLedgerEntry()
    {
    }

    public Guid RunId { get; private init; }

    /// <summary>Null for the pre-submission estimate; the batch id once the batch is recorded (ADR-F3-0002).</summary>
    public Guid? BatchId { get; private init; }

    public BatchStage Stage { get; private init; }

    public ModelTier Tier { get; private init; }

    public LedgerEntryKind Kind { get; private init; }

    public decimal CostUsd { get; private init; }

    public int InputTokens { get; private init; }

    public int OutputTokens { get; private init; }

    public DateTimeOffset RecordedAt { get; private init; }
}
