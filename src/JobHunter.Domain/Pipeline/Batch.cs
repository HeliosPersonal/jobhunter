using JobHunter.Domain.Common;

namespace JobHunter.Domain.Pipeline;

/// <summary>
/// One submission to the provider's Message Batches API (data-model §batches). The
/// <see cref="ProviderBatchId"/> is the resumability anchor — persisted the instant the provider
/// accepts the submission, so a crash between "submitted" and "recorded" is recoverable by re-reading
/// the batch rather than by resubmitting and paying twice (QG-1). The unique
/// <c>(run_id, stage, tier)</c> index behind this aggregate is what makes double submission impossible
/// rather than merely unlikely: a resumed Run that tried to resubmit would violate it and fail loudly.
///
/// <para>The same machinery serves F4/F5/F8 unchanged — they differ only in <see cref="Stage"/> and
/// <see cref="Tier"/>. The aggregate depends on nothing external.</para>
/// </summary>
public sealed class Batch : Entity
{
    public static readonly Error IllegalStateTransition =
        new("batch.state.illegal", "The attempted Batch state transition is not permitted.");

    private static readonly HashSet<(BatchState From, BatchState To)> LegalStates =
        new()
        {
            (BatchState.Submitted, BatchState.InProgress),
            (BatchState.Submitted, BatchState.Completed),
            (BatchState.Submitted, BatchState.Failed),
            (BatchState.Submitted, BatchState.Expired),
            (BatchState.InProgress, BatchState.Completed),
            (BatchState.InProgress, BatchState.Failed),
            (BatchState.InProgress, BatchState.Expired),
        };

    public Batch(
        Guid id,
        Guid runId,
        BatchStage stage,
        ModelTier tier,
        string providerBatchId,
        string promptVersion,
        int itemCount,
        DateTimeOffset submittedAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerBatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemCount);

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A Batch must belong to a Run.", nameof(runId));
        }

        RunId = runId;
        Stage = stage;
        Tier = tier;
        ProviderBatchId = providerBatchId;
        PromptVersion = promptVersion;
        ItemCount = itemCount;
        SubmittedAt = submittedAt;
        State = BatchState.Submitted;
    }

    private Batch()
    {
    }

    public Guid RunId { get; private set; }

    public BatchStage Stage { get; private init; }

    public ModelTier Tier { get; private init; }

    /// <summary>The resumability anchor — persisted immediately on submit (data-model §batches).</summary>
    public string ProviderBatchId { get; private set; } = null!;

    public BatchState State { get; private set; }

    public string PromptVersion { get; private set; } = null!;

    public int ItemCount { get; private set; }

    /// <summary>As reported by the provider on retrieval; null until then.</summary>
    public int? InputTokens { get; private set; }

    public int? OutputTokens { get; private set; }

    /// <summary>A flat counter over time means the poller has stopped (runbook R2).</summary>
    public int PollAttempts { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Records one poll pass. Idempotent-safe: it only ever counts up.</summary>
    public void RecordPoll() => PollAttempts++;

    /// <summary>
    /// Moves the batch to <paramref name="to"/>. Returns a failure — never throws — when the pair is not
    /// a legal edge (already-terminal batches have no outgoing edges). Reaching
    /// <see cref="BatchState.Completed"/> is the only state that stamps <see cref="CompletedAt"/> and the
    /// provider token counts, which is why they arrive together on retrieval.
    /// </summary>
    public Result<Batch> TransitionTo(
        BatchState to,
        DateTimeOffset at,
        int? inputTokens = null,
        int? outputTokens = null)
    {
        if (!LegalStates.Contains((State, to)))
        {
            return new Error(
                IllegalStateTransition.Code,
                $"Illegal Batch state transition {State} -> {to}.");
        }

        State = to;
        if (to == BatchState.Completed)
        {
            CompletedAt = at;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
        }

        return Result<Batch>.Success(this);
    }
}
