# T09 — RunOrchestrator: start, scope, resume

**Layer:** app · **Deps:** T04, T06 · **Est:** M · **Owner:** Viacheslav

## What

The daily Hangfire schedule at 02:00, Run creation with the ceiling snapshotted, scope
selection from `cutoff_from` (the previous Run's `cutoff_to`, so a skipped day is caught up rather
than lost), and — the important half — the startup path that loads every non-terminal Run and
re-enters it at its current state.

## Done when

- A Run's scope is exactly the jobs discovered in its cut-off window; a skipped day is caught up.
- On startup, every non-terminal Run resumes at its current state (AC-05).
- Zero jobs in scope completes the Run without submitting and without error.
- A second Run cannot be created while one is live.
- Start, resume and abort are operator-scoped (AC-12).
- Before submitting, the orchestrator reconciles against the provider's recent batches to close the crash-window in SAD §11 D5.

## Links

[[../adr/0001-run-as-resumable-state-machine|ADR-F3-0001]] · [[../sad]] §6.1
