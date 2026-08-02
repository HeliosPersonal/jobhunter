# T04 — Migration and repositories for Run, Batch, Ledger

**Layer:** infra/db · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

Migration `F3_AddRunsBatchesLedger` creating `runs`, `batches`, `batch_items` and
`cost_ledger_entries` with every index from [[../data-model|data-model]] — including the two that
carry invariants: the partial unique index allowing one live Run, and unique
`(run_id, stage, tier)` on batches.

## Done when

- Migration applies on a clean database; all ten indexes exist with their declared names.
- Creating a second non-terminal Run is rejected by the partial unique index.
- Inserting a second batch for the same run, stage and tier is rejected — asserted by violating it.
- `cost_ledger_entries` has no update or delete path in the repository.
- The resumable-Runs query is covered by its partial index, verified with a query plan assertion.

## Links

[[../data-model]] · [[../../f0-platform-foundation/tasks/T07-persistence-conventions|F0 T07]]
