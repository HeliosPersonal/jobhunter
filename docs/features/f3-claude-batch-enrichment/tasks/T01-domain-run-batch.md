# T01 — Domain: Run, RunState, Batch, BatchItem

**Layer:** domain · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

The `Run` aggregate with its explicit state machine, plus `Batch`, `BatchItem`,
`BatchState` and `ModelTier`. The transition table is data, not scattered `if` statements, so an
illegal transition is one rejection point rather than an emergent property.

## Done when

- Every legal transition from [[../sad|SAD]] §6.1 is permitted; every other is rejected with the attempted pair named.
- An exhaustive test walks all state pairs and asserts legality against the table.
- `Run.Abort(reason)` from any non-terminal state produces `CostAborted` or `Failed` and is idempotent.
- `ceiling_usd` is captured at construction and cannot be changed afterwards.
- The aggregate has no dependency on EF Core, Anthropic or Wolverine.

## Links

[[../adr/0001-run-as-resumable-state-machine|ADR-F3-0001]] · [[../data-model]]
