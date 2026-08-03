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
}
