# T08 — Reconcile and rebuild

**Layer:** search · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

The nightly reconcile comparing counts and re-indexing drift, and the one-command full
rebuild. A rebuild is a routine operation rather than a recovery procedure, which is what makes the
index disposable.

## Done when

- Reconcile detects divergence above 1% and re-indexes the affected window (AC-10).
- `jobhunter.index.drift` is exported so drift that does not self-heal is visible.
- A full rebuild reconstructs the collection with **document-by-document equivalence**, not merely a matching count (QG-1).
- Rebuild completes in under 10 minutes for 10 000 jobs.
- Rebuild takes a lock; a concurrent reconcile skips and logs.
- Deleting the collection entirely and rebuilding loses nothing.

## Links

[[../adr/0001-index-as-rebuildable-projection|ADR-F9-0001]] · [[../../../operations/runbooks|R8]]
