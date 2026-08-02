# T08 — Orchestration, warnings and stage feedback

**Layer:** app · **Deps:** T07, T05 · **Est:** M · **Owner:** Viacheslav

## What

`ResearchOrchestrator` tying the flow together, surfacing warnings first, and feeding the
funding stage back onto the company record — the one cross-feature write in F8, made deliberately so
that better data improves ranking rather than only the dossier.

## Done when

- Layoffs and funding difficulty are marked as warnings and rendered first (AC-04).
- The funding category updates `companies.stage`; a disagreement resolves to the newer observation and is recorded (AC-10).
- Categories with no claims are recorded as unavailable rather than omitted (AC-07).
- `ResearchCompleted` is published once per dossier.
- The handler is idempotent on `(company_id, run_id)`.

## Links

[[../sad]] §6.1 · [[../data-model]]
