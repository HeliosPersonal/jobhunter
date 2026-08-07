# T08 — Orchestration, warnings and stage feedback

**Layer:** app · **Deps:** T07, T05 · **Est:** M · **Owner:** Viacheslav

## What

`ResearchOrchestrator` tying the flow together, surfacing warnings first, and feeding the
firmographics back onto the company record — the whitelisted cross-owner write in F8
([[../../../AUDIT-RESOLUTION-DECISIONS]] §1, owner F1 acknowledges), made deliberately so that better
data improves ranking rather than only the dossier.

## Done when

- Layoffs and funding difficulty are marked as warnings and rendered first (AC-04).
- The funding category updates `companies.stage` and the size category updates `companies.employee_band`;
  a disagreement resolves to the newer observation and is recorded (AC-10).
- Categories with no claims are recorded as unavailable rather than omitted (AC-07).
- `ResearchCompleted` is published once per dossier.
- The handler is idempotent on `(company_id, run_id)`.

## Links

[[../sad]] §6.1 · [[../data-model]]

## Implementation

The orchestration flow is split into a deterministic core and the thin transactional edge that will wire it
onto the bus, the same shape as T05 and T07 — the pure pieces are unit-tested to the boundaries here, and the
`RankingCompleted` handler, the synthesis batch submit/poll and the Dapper candidate projection land with the
Worker wiring (they need a database and the F3/F4 batch machinery, which is where the integration suite lives).

- **Firmographics on the record (AC-10).** `record_research` was reopened to emit an optional `stage`
  (enum from `CompanyStage`) and `employeeBand` (bounded string), and `PromptVersion` bumped to `research-v2`;
  the system prompt classifies both strictly from the fetched text, omitting either when there is no evidence
  (never from memory). `Company.ApplyFirmographics(stage, band, observedAt)` is the whitelisted cross-owner
  write: it lands only when the observation is strictly newer than the one already recorded, so a disagreement
  resolves to the newer observation and a re-run of an older dossier never overwrites a fresher classification.
  A new nullable `companies.firmographics_observed_at` column arbitrates that comparison.
- **The dossier assembler.** `ResearchDossierAssembler.Assemble(ResearchDossierInput)` is a pure function of
  what the fetchers found and what the synthesiser returned. Every fetched document becomes a `ResearchSource`
  *before* verification, so "did the model invent this" is a set-membership check (QG-1); the claims go
  through the T07 `ClaimVerifier`, the survivors become `ResearchClaim`s resting on their source, the rest are
  counted as discarded. Every category with no *surviving* claim — including one whose only claim was
  discarded — is recorded as unavailable (AC-07). Warnings-first ordering and "every claim rests on a recorded
  source" are the aggregate's own invariants, so the assembler only ever feeds it verified material.
- **Feedback out.** `ResearchFeedback.ApplyFirmographics` applies the synthesis's firmographics to the company
  at the dossier's generation instant, and `ResearchFeedback.CompletedEvent` mints `ResearchCompleted`
  (`RunId`, `CompanyId`, `ResearchId`, verified `ClaimCount`) — published once per dossier, idempotency key
  `(company_id, run_id)`, which the one-dossier-per-pair constraint makes uncollidable. An empty dossier still
  completes with a zero count so the digest is never left in silence.
