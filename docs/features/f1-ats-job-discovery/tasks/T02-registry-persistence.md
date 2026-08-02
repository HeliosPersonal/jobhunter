# T02 — Migration and repositories for registry tables

**Layer:** infra/db · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

Migration `F1_AddRegistryAndRawPostings` creating `companies`, `ats_bindings`,
`job_sources`, `raw_postings` and `source_fetch_log` with every index from
[[../data-model|data-model]]. Repositories for company and binding writes; a Dapper query for the
cycle fan-out.

## Done when

- Migration applies on a clean database; all eight indexes exist with the declared names.
- The partial unique index on live bindings rejects a duplicate live binding.
- `RawPostingRepository` exposes **no** method that writes `payload` after insert (AC-10).
- The fan-out query returns only active companies with a confident, non-quarantined binding.
- Integration tests cover each constraint by violating it.

## Links

[[../data-model]] · [[../../f0-platform-foundation/tasks/T07-persistence-conventions|F0 T07]]
