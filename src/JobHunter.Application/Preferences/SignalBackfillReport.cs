namespace JobHunter.Application.Preferences;

/// <summary>
/// The outcome tally of one signal-backfill run (F7 T03, done-when 5): how many historical outcomes were
/// examined, how many minted a new signal, how many were skipped because a signal already existed (the
/// idempotence path — a second run reports all-skipped), and how many could not be snapshotted because the
/// job has since closed. A value the operator command prints — the backfill reports its work rather than
/// being silent.
/// </summary>
public sealed record SignalBackfillReport(int Examined, int Captured, int Skipped, int WithoutFacts);
