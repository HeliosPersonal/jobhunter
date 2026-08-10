# T13 — Closure sweep: emit JobClosed for postings gone from their board

**Layer:** app · **Deps:** T11 · **Est:** S · **Owner:** Viacheslav

## What

`ClosureSweepHandler`: after a discovery cycle completes for a source, detect postings that were
previously live but no longer appear on the board — a `raw_postings.last_seen_at` (and per-alias
`job_aliases.last_seen_at`) that did not advance this cycle — and publish `JobClosed`. This is the F1
task that actually emits `JobClosed`, declared an F1 output in the epic and named in the event catalog
with `ClosureSweepHandler` as publisher.

## Done when

- A posting absent from its board for a full cycle produces exactly one `JobClosed`
  (`JobId`, `ClosedAt`, `Reason`), keyed idempotently on `(JobId, ClosedAt)`.
- A posting that reappears before the sweep does **not** emit `JobClosed`.
- The sweep keys on `last_seen_at`, which the T11 upsert bumps on every unchanged re-fetch — so a
  `DO NOTHING` insert would have broken it.
- `JobClosed` is consumed downstream by `SearchIndexer` and `JobClosureHandler` (event catalog);
  F1 only produces it.
- An idempotency test runs the sweep twice and asserts a single `JobClosed` per closed posting.

## Out of scope

- Reopening a closed job (a new posting creates a new lifecycle).
- Any consumer of `JobClosed` (F2, F6, F9 own those).

## Links

[[../sad]] §6.1 · [[../../../architecture/event-catalog]] · [[../data-model]]
