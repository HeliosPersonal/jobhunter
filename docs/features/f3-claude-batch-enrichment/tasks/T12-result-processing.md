# T12 — Result processing, per-item isolation and retry

**Layer:** app · **Deps:** T11, T05 · **Est:** L · **Owner:** Viacheslav

## What

Stream results, parse each item independently, upsert valid enrichments, record failures
with their raw content and error, write the `Actual` ledger entry, and advance the Run. Plus the
retry sweep that gives a failed item exactly one more attempt, at cheap tier, in the next Run.

## Done when

- A mixed batch stores the valid items, records each bad one, and completes the Run (AC-07, QG-3).
- Reprocessing the same results produces no duplicate enrichments and no extra ledger entry (AC-06).
- `Actual` cost is written from the provider's reported usage, and cost is attributable per stage and tier (AC-10).
- A failed item retries once next Run; a twice-failed item is `Abandoned` and never retried again (AC-08).
- `raw_result` is retained for failed items only, and pruned after 30 days.
- The handler is idempotent — the whole of the crash matrix checkpoints 7 and 8.

## Links

[[../test-plan]] §The crash matrix · [[../sad]] §10 QG-3
