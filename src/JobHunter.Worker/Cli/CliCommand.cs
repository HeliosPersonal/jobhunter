namespace JobHunter.Worker.Cli;

/// <summary>The operational verbs the Worker exposes (SAD §5). A recognised verb runs and exits.</summary>
public enum CliCommand
{
    /// <summary>Apply outstanding EF Core migrations, then exit. Run as a pre-deploy Job (S4/AC-11).</summary>
    Migrate,

    /// <summary>List or re-enqueue dead-lettered messages (T16, runbook R6).</summary>
    ReplayDlq,

    /// <summary>Upsert the curated company registry from the seed file, then exit (F1 T03).</summary>
    Seed,

    /// <summary>
    /// Re-run normalisation and deduplication over stored raw payloads with zero network, preserving job
    /// identities where the fingerprint is unchanged (F2 T09, AC-09). Operator-scoped.
    /// </summary>
    Reprocess,

    /// <summary>Prune raw postings older than the retention window (90 days), then exit (F2 T09, O3).</summary>
    PruneRaw,

    /// <summary>
    /// Replay application outcomes recorded before signal staging into signals, then exit (F7 T03, done-when 5).
    /// Idempotent — a re-run captures nothing more. Scope with <c>--since &lt;yyyy-MM-dd&gt;</c>. Operator-scoped.
    /// </summary>
    BackfillSignals,
}
