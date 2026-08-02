# T05 — Migration and repository for enrichments

**Layer:** infra/db · **Deps:** T02, T04 · **Est:** S · **Owner:** Viacheslav

## What

Migration `F3_AddEnrichments` plus the repository, whose only write path is an upsert on
`(job_id, run_id)`. That single choice is what makes replaying a half-processed result set safe
(AC-06) rather than duplicating.

## Done when

- Unique `(job_id, run_id)` enforced ([[../../../CONTEXT]] invariant 3), asserted by violating it.
- The upsert is idempotent — calling it twice with the same payload leaves one row unchanged.
- `reasons` round-trips as a JSON array and rejects an empty array at the domain boundary.
- `prompt_version` is non-null on every row (AC-11).

## Links

[[../data-model]] §enrichments
