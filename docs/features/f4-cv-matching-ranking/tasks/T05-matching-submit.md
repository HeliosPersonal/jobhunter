# T05 — Matching submission through the F3 Run machinery

**Layer:** app · **Deps:** T04, T02 · **Est:** M · **Owner:** Viacheslav

## What

`MatchingSubmitHandler` consuming `EnrichmentCompleted`: build deep-tier items from the
Run scope plus enrichments plus the active CV, estimate, ledger, check the ceiling, submit. Uses F3's
`ILlmBatchClient`, `CostAccountant` and poller **unchanged** — this task adds a stage, not a mechanism.

## Done when

- No F3 file is modified — the stage is added by configuration and a new handler.
- The ceiling gate behaves identically to enrichment: over budget means the client is never called.
- Jobs without an enrichment are included with the reduced-confidence path, not skipped (AC-09).
- Unique `(run_id, Matching, Deep)` prevents any double submission.
- Matching cost stays under $0.35 at 150 jobs, asserted against the pricing table.

## Links

[[../sad]] §6.1 · [[../../f3-claude-batch-enrichment/tasks/T10-enrichment-submit|F3 T10]]
