# T02 — Migration and repositories

**Layer:** infra/db · **Deps:** T01 · **Est:** S · **Owner:** Viacheslav

## What

Migration `F6_AddApplications` with the six indexes, and repositories where
`application_transitions` has **no update and no delete path** — the property that makes the history
trustworthy rather than merely present.

## Done when

- Migration applies on a clean database; all six indexes exist with declared names.
- One application per job is enforced; asserted by violating it.
- The transitions repository exposes no update and no delete method — asserted by an architecture test.
- The reminder-sweep query is covered by `idx_applications_due`, verified with a query plan assertion.
- The pipeline query is covered by `idx_applications_pipeline`.

## Links

[[../data-model]]
