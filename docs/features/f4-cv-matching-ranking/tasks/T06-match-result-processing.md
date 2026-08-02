# T06 — Match result processing

**Layer:** app · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

Process the streamed match results through F3's per-item isolation: upsert valid matches,
record failures with their content, publish `MatchingCompleted`. Same idempotency guarantees as
enrichment, on `(job_id, run_id, profile_id)`.

## Done when

- A malformed item affects only its own job; the rest of the day's matches are stored (AC-10).
- Reprocessing the same results produces no duplicate matches.
- `cv_version_id` is stamped on every match, so re-staling can find them later.
- The handler is idempotent, asserted by running it twice.
- Matches with no reasons are recorded failed rather than persisted (AC-02).

## Links

[[../../f3-claude-batch-enrichment/tasks/T12-result-processing|F3 T12]]
