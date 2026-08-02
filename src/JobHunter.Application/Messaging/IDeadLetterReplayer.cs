namespace JobHunter.Application.Messaging;

/// <summary>
/// Lists and re-enqueues dead-lettered messages (T16, runbook R6). The port lives in Application; the
/// broker adapter is internal to Infrastructure and reached through this interface (architecture rule
/// 8). Replay is safe against the consumer inbox — a message already processed is deduplicated before
/// its handler runs, so a replay is a no-op rather than a duplicate side effect (invariant 8).
/// </summary>
public interface IDeadLetterReplayer
{
    /// <summary>Counts dead-lettered messages per dead-letter queue, grouped by queue name.</summary>
    Task<IReadOnlyList<DeadLetterSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves every message on <paramref name="deadLetterQueue"/> back onto its source queue and returns
    /// how many were moved. Refuses an unknown or empty queue with a <see cref="ReplayOutcome"/>.
    /// </summary>
    Task<ReplayResult> ReplayQueueAsync(string deadLetterQueue, CancellationToken cancellationToken = default);
}

/// <summary>One dead-letter queue and its depth, with the source queue a replay would target.</summary>
public sealed record DeadLetterSummary(string DeadLetterQueue, string SourceQueue, uint MessageCount);

/// <summary>The result of a replay attempt. An outcome value, never an exception (coding-standards §5).</summary>
public sealed record ReplayResult(ReplayOutcome Outcome, int MovedCount, string? Message = null)
{
    public static ReplayResult Moved(int count) => new(ReplayOutcome.Replayed, count);

    public static ReplayResult UnknownQueue(string queue) =>
        new(ReplayOutcome.UnknownQueue, 0, $"Queue '{queue}' does not exist.");

    public static ReplayResult Empty(string queue) =>
        new(ReplayOutcome.EmptyQueue, 0, $"Queue '{queue}' has no dead-lettered messages.");
}

/// <summary>Why a replay did or did not move messages.</summary>
public enum ReplayOutcome
{
    Replayed,
    UnknownQueue,
    EmptyQueue,
}
