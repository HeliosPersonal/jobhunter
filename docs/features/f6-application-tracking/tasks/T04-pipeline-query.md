# T04 — Pipeline query and history view

**Layer:** app · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

The Dapper projection grouping applications by status, most recently active first, plus
the single-application view with its full transition history and notes. `daysInStage` is computed at
read time rather than stored, because storing it would mean keeping it current.

## Done when

- Applications are grouped by status with counts, most recently active first (AC-01).
- Archived applications are excluded from the pipeline view but retrievable by id.
- The history view lists every transition with its time and source, in order (AC-03).
- 200 applications render in under 500 ms, with the index confirmed by a query plan assertion.
- The query is Dapper and read-only — no write path in the queries folder.

## Links

[[../contracts/application-api]] · [[../../f0-platform-foundation/tasks/T07-persistence-conventions|F0 T07]]
