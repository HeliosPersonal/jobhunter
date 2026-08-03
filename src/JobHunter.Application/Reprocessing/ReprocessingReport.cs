namespace JobHunter.Application.Reprocessing;

/// <summary>
/// The outcome tally of one reprocessing run (AC-09): how many jobs were examined, how many kept their id
/// because the recomputed fingerprint was unchanged, how many were superseded by a new job under a changed
/// fingerprint, and how many could not be recomputed (a vanished or unparseable stored payload). A value the
/// operator command prints — reprocessing reports its work rather than being silent.
/// </summary>
public sealed record ReprocessingReport(int Examined, int Unchanged, int Superseded, int Failed);
