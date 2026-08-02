# T07 — Operational endpoints

**Layer:** api · **Deps:** T04, T02 · **Est:** M · **Owner:** Viacheslav

## What

Every action the runbooks call for, as an endpoint: resume a run, redeliver a digest,
rebuild the index, unquarantine a source, reprocess a window, and read corpus statistics. Recovery
should not require database access.

## Done when

- Every runbook action in [[../../../operations/runbooks|R1, R4, R8]] has a corresponding endpoint.
- All require `jobhunter:admin`; with only read scope they are refused (AC-07).
- `redeliver` is safe by construction — the delivery log prevents re-sending already-delivered cards.
- `reindex` takes a lock so a concurrent reconcile skips rather than colliding.
- Each endpoint returns a job identifier for long-running operations rather than blocking.
- The runbooks are updated to reference these endpoints instead of database access.

## Links

[[../../../operations/runbooks]] · [[../contracts/openapi|API contract]] §Operations
