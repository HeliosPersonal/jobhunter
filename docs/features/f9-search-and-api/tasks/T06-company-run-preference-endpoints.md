# T06 — Company, run and preference endpoints

**Layer:** api · **Deps:** T04 · **Est:** M · **Owner:** Viacheslav

## What

Company detail and research, run listing and detail, and the preference endpoints that
render each learned weight with its supporting evidence — the API face of F7's explainability contract.

## Done when

- Company detail includes the ATS binding, live jobs and the latest dossier with claim dates.
- Run detail exposes batches, the cost ledger and per-stage timings.
- `GET /api/preferences` renders each weight with its one-sentence explanation and evidence count.
- `GET /api/preferences/suppressed` lists what was hidden and why, so suppression regret is measurable.
- Read endpoints require `jobhunter:read`; state-changing ones require `jobhunter:admin` (AC-07).
- All read models come from Dapper queries against PostgreSQL, never from the index (SAD §4 S5).

## Links

[[../contracts/openapi|API contract]] · [[../../f7-preference-learning/adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]]
