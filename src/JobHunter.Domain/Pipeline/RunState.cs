namespace JobHunter.Domain.Pipeline;

/// <summary>
/// The state of a daily <see cref="Run"/> (data-model §runs <c>state</c>,
/// AUDIT-RESOLUTION-DECISIONS §2 — the canonical nine values). Persisted as <c>text</c>, never an
/// ordinal (coding-standards §5). Only <see cref="Delivered"/>, <see cref="Failed"/> and
/// <see cref="CostAborted"/> are terminal; the orchestrator resumes every other state on startup
/// (ADR-F3-0001). There is no <c>Discovering</c> value — discovery is F1/F2, upstream of the Run.
/// </summary>
public enum RunState
{
    /// <summary>Just created; scope not yet selected, nothing submitted.</summary>
    Created,

    /// <summary>The enrichment batch has been submitted and is being polled/retrieved (F3).</summary>
    Enriching,

    /// <summary>The matching batch stage (F4).</summary>
    Matching,

    /// <summary>Ranking the matched jobs (F4/F7).</summary>
    Ranking,

    /// <summary>Company research for the top jobs (F8).</summary>
    Researching,

    /// <summary>Assembling the digest (F5).</summary>
    Reporting,

    /// <summary>The digest was delivered — terminal.</summary>
    Delivered,

    /// <summary>An unrecoverable error ended the Run — terminal.</summary>
    Failed,

    /// <summary>
    /// The cost ceiling would have been breached, so the Run was stopped before spending — terminal for
    /// the cost path. A reduced digest still ships via the <c>RunCostAborted</c> handler (SAD §6.1).
    /// </summary>
    CostAborted,
}
