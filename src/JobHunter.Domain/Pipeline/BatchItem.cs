using JobHunter.Domain.Common;

namespace JobHunter.Domain.Pipeline;

/// <summary>
/// One item inside a <see cref="Batch"/> — one job's request and its eventual per-item outcome
/// (data-model §batch_items). One row per item is what makes per-item failure isolation possible: a
/// malformed result is one <see cref="BatchItemState.ParseFailed"/> row, not a failed Run (QG-3). The
/// <see cref="CustomId"/> is the job id verbatim, so a provider result maps back with no lookup table.
///
/// <para>A failed item is retried exactly once, on the next Run; a second failure abandons it (AC-08).
/// <see cref="RawResult"/> is retained only for failed items, and only long enough to diagnose — a
/// successful item's parsed form <em>is</em> the enrichment, so keeping its raw payload would be
/// duplication.</para>
/// </summary>
public sealed class BatchItem : Entity
{
    /// <summary>The retry ceiling: an item that fails twice across two Runs is abandoned (AC-08).</summary>
    public const int MaxRetries = 1;

    public BatchItem(Guid id, Guid batchId, string customId, Guid jobId)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customId);

        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("A BatchItem must belong to a Batch.", nameof(batchId));
        }

        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A BatchItem must reference a Job.", nameof(jobId));
        }

        BatchId = batchId;
        CustomId = customId;
        JobId = jobId;
        State = BatchItemState.Pending;
    }

    private BatchItem()
    {
    }

    public Guid BatchId { get; private set; }

    /// <summary>The job id verbatim, so a result maps back with no lookup table.</summary>
    public string CustomId { get; private set; } = null!;

    public Guid JobId { get; private set; }

    public BatchItemState State { get; private set; }

    /// <summary>Retained only for failed items, 30 days (data-model §batch_items).</summary>
    public string? RawResult { get; private set; }

    /// <summary>What was wrong, in one line.</summary>
    public string? ParseError { get; private set; }

    public int RetryCount { get; private set; }

    /// <summary>Marks the item parsed and validated into an enrichment. Clears any prior failure detail.</summary>
    public void MarkParsed()
    {
        State = BatchItemState.Parsed;
        RawResult = null;
        ParseError = null;
    }

    /// <summary>
    /// Marks the item failed to parse, retaining <paramref name="parseError"/> and the raw payload for
    /// diagnosis. The item is eligible for one retry on the next Run.
    /// </summary>
    public void MarkParseFailed(string parseError, string? rawResult)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parseError);
        State = BatchItemState.ParseFailed;
        ParseError = parseError;
        RawResult = rawResult;
    }

    /// <summary>The provider reported an error for this specific item; it retries once next Run.</summary>
    public void MarkProviderError(string parseError, string? rawResult)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parseError);
        State = BatchItemState.ProviderError;
        ParseError = parseError;
        RawResult = rawResult;
    }

    /// <summary>
    /// Prepares the item for its one permitted retry on the next Run: increments the counter and returns
    /// it to <see cref="BatchItemState.Pending"/>. Once the counter reaches <see cref="MaxRetries"/> the
    /// item is abandoned instead (AC-08); returns <see langword="false"/> and leaves the item
    /// <see cref="BatchItemState.Abandoned"/> so the caller stops retrying.
    /// </summary>
    public bool TryScheduleRetry()
    {
        if (RetryCount >= MaxRetries)
        {
            State = BatchItemState.Abandoned;
            return false;
        }

        RetryCount++;
        State = BatchItemState.Pending;
        RawResult = null;
        ParseError = null;
        return true;
    }
}
