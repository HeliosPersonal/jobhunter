namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The port the operational endpoints enqueue long-running recovery work through (F9 operational
/// endpoints, runbooks R4/R8). A full reindex or a history reprocess can take minutes, so the endpoint
/// must not block the request thread: it hands the work to this port and returns the operation id the
/// caller can quote when checking progress. The Hangfire-backed implementation lives in Infrastructure —
/// storage is present in every host so a job can be enqueued from the Api and run on the Worker's server
/// (ADR-0004) — and the port keeps the endpoint free of any scheduler type.
/// </summary>
public interface IOperationScheduler
{
    /// <summary>Enqueues a full search-index rebuild and returns its operation id (runbook R8).</summary>
    string EnqueueReindex();

    /// <summary>
    /// Enqueues a reprocess of every job first seen at or after <paramref name="firstSeenFrom"/> and
    /// returns its operation id (F2 AC-09, runbook R4). The window bounds the offline recompute.
    /// </summary>
    string EnqueueReprocess(DateTimeOffset firstSeenFrom);
}
