# T02 — Migration and repositories for profiles, CVs, matches, scores

**Layer:** infra/db · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

Migration `F4_AddProfilesCvsMatchesScores` with all eight indexes, including the three
partial unique indexes that carry invariants: one active profile, one active CV per profile, one
match per job-run-profile.

## Done when

- Migration applies on a clean database; all eight indexes exist with declared names.
- Attempting a second active profile or a second active CV is rejected — asserted by violating each.
- The digest query index covers `(run_id, final_score DESC) WHERE NOT suppressed`, verified with a query plan assertion.
- `extracted_text` exists on exactly one table in the whole schema — asserted by a schema test.
- Match upsert on `(job_id, run_id, profile_id)` is idempotent.

## Links

[[../data-model]] · [[../../f0-platform-foundation/tasks/T07-persistence-conventions|F0 T07]]
