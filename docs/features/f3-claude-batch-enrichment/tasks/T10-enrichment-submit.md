# T10 — Enrichment submission with the cost gate

**Layer:** app · **Deps:** T07, T08, T09 · **Est:** M · **Owner:** Viacheslav

## What

Build the batch from the Run's scope, estimate, **write the estimate to the ledger
before calling the client**, check the ceiling, submit, and persist the provider batch id
immediately. The ordering here is the entire content of
[[../adr/0002-pre-submission-cost-ceiling|ADR-F3-0002]].

## Done when

- When the estimate would breach the ceiling, `SubmitAsync` is **never invoked** — asserted with a throwing fake (AC-03, QG-2).
- The estimated ledger entry is committed before the client call in every path (AC-04).
- The provider batch id is persisted in the same transaction that records the batch.
- A breach produces `CostAborted` plus `RunCostAborted`, and the digest still ships reduced.
- `custom_id` is the job id, so results map back without a lookup table.
- Failed items from the previous Run are included for their single retry (AC-08).

## Links

[[../adr/0002-pre-submission-cost-ceiling|ADR-F3-0002]] · [[../sad]] §6.2
