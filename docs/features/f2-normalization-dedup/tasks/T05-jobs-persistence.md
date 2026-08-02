# T05 — Migration and repositories for jobs and aliases

**Layer:** infra/db · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

Migration `F2_AddJobsAndAliases` creating `jobs`, `job_aliases` and `job_technologies`
with all seven indexes, enabling `pg_trgm` for the near-duplicate index. Repository with the
conflict-tolerant insert path, plus the `LiveJobsQuery` Dapper read model.

## Done when

- Migration applies on a clean database; the trigram extension is created idempotently.
- The unique fingerprint index rejects a duplicate; the test asserts it by violating it.
- The insert reports whether it inserted or conflicted in one round trip — no read-then-write.
- `LiveJobsQuery` excludes closed jobs and is covered by the partial index, verified with a query plan assertion.
- `job_aliases` has no delete path.

## Links

[[../data-model]] · [[../../f0-platform-foundation/tasks/T07-persistence-conventions|F0 T07]]
