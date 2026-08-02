---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f4-cv-matching-ranking, mvp, jobhunter]
---

# PRD — f4-cv-matching-ranking

> **Inputs:** [[../../CONTEXT]] §1 (Profile, CV, Match, Score) · [[../f3-claude-batch-enrichment/sad|F3 SAD]] · [[../../00-overview/sad|SAD]] §6.2, §6.3
> **External context:** [[../../DECISION-LOG|D5]] (precision@10 is the KPI), [[../../ARCHITECTURE-OPEN-DECISIONS|O5, O10]]

## 1. Context

Everything before this stage is plumbing. F1 acquires, F2 canonicalises, F3 describes — none of it
answers the only question the Owner actually has at 07:00: *is this worth my time?*

Answering it requires the one piece of information the rest of the pipeline deliberately avoids: the
CV. That asymmetry is a design decision, not an accident. Enrichment describes the job so it can be
cached, shared and reasoned about without personal data anywhere near it. Matching is where the
personal data enters, once, in one prompt, from one stage — which is what makes the claim "the CV
crosses exactly one boundary" auditable rather than aspirational
([[../../engineering/security]] §1).

The second half of the feature is arithmetic, and it is arithmetic on purpose. The model produces a
judgement; the Score that orders the digest is computed by us from that judgement, the learned
preference weights and freshness. Keeping the final ordering out of the model's hands is what makes
it explainable, testable against a golden set, and tunable without a prompt change
([[adr/0001-explainable-linear-scoring|ADR-F4-0001]]).

`precision@10` is the product's KPI ([[../../DECISION-LOG|D5]]) and this is the stage that owns it.

## 2. Goals

- Compare every enriched job against the Owner's current CV and produce a fit judgement with a
  score, the missing skills, a realistic interview probability, a salary expectation and reasons.
- Combine that judgement with learned preferences and freshness into one ordering number.
- Make every ordering decision explainable from stored data — no unexplained number reaches the Owner.
- Keep the CV's exposure to exactly one prompt from exactly one stage.
- Keep matches honest when the CV changes.

## 3. Non-goals

- Describing the job — F3 owns enrichment, and it does so without the CV.
- Learning the preference weights — F7 fits them; F4 consumes the active model.
- Deciding what to show or how — F5 owns the digest.
- Rewriting the CV or advising on it. A CV gap report is in [[../../BACKLOG]].
- Multiple personas or target profiles. One active Profile ([[../../ARCHITECTURE-OPEN-DECISIONS|O11]]).

## 4. User stories

### US-01: Know whether a job fits me specifically
**As the** Owner **I want** each job scored against my actual experience **so that** I am not reading
roles that were never plausible.

### US-02: Know what I am missing
**As the** Owner **I want** the gaps named **so that** I can judge whether a stretch role is worth an
application or a skill worth learning.

### US-03: Know my realistic odds
**As the** Owner **I want** an honest interview probability **so that** I can spend my applications
where they might land.

### US-04: Understand the ordering
**As the** Owner **I want** to see why one job outranks another **so that** I can trust the order
rather than re-reading everything.

### US-05: Have my preferences respected
**As the** Owner **I want** the ordering to reflect what I have shown I care about **so that** it
improves as I use it.

### US-06: Keep my CV private
**As the** Owner **I want** my CV used only where it is needed **so that** it is not scattered across
logs, indexes and third parties.

### US-07: Not be misled by a stale match
**As the** Owner **I want** matches recomputed when my CV changes **so that** an old judgement about
an old me is not presented as current.

## 5. Acceptance criteria

### AC-01 (US-01, US-02, US-03) — happy path
**Given** a job with a completed assessment and an active CV
**When** matching completes for the day
**Then** the job carries exactly one match for that day, stating a fit score, the missing skills, an
interview probability, a salary expectation and reasons.

### AC-02 (US-04) — domain invariant
**Given** any match
**When** it is stored
**Then** it carries at least one reason; a match with no reasons is rejected and recorded as failed
rather than persisted.

### AC-03 (US-04) — domain invariant
**Given** an ordering number presented to the Owner
**When** its derivation is inspected
**Then** every contributing component and its weight is recorded, and the components account for the
whole number.

### AC-04 (US-05) — cross-context
**Given** an active set of learned preferences
**When** ordering is computed
**Then** the preferences influence the order, and the influence of each is recorded per job.

### AC-05 (US-05) — domain invariant
**Given** a job that preferences would push below the presentation threshold
**When** ordering is computed
**Then** the job is recorded as suppressed with the reason, and it remains retrievable rather than
deleted.

### AC-06 (US-06) — authorization
**Given** the Owner's CV content
**When** the system operates normally
**Then** the CV content appears only in the comparison sent to the assessment provider, and never in
any log, trace, search index, notification or stored artifact other than the CV record itself.

### AC-07 (US-06) — authorization
**Given** a request to upload, replace or read a CV
**When** it arrives without owner credentials
**Then** it is refused and the stored CV is unchanged.

### AC-08 (US-07) — cross-context
**Given** matches produced against a previous version of the CV
**When** a new CV version is activated
**Then** those matches are marked as no longer current, and recent live jobs are re-matched against
the new version.

### AC-09 (US-01) — error path
**Given** a job whose assessment failed or is missing
**When** matching runs
**Then** the job is still matched using what is known about it, and the absence of an assessment is
recorded as reducing confidence rather than skipping the job.

### AC-10 (US-03) — error path
**Given** a comparison result that does not conform to the expected shape
**When** results are processed
**Then** that job has no match for the day, the failure is recorded with its content, and the rest of
the day's matches are unaffected.

### AC-11 (US-04) — happy path
**Given** a day's matches and the active preferences
**When** ordering completes
**Then** every non-suppressed job for that day has exactly one ordering number, and the same inputs
always produce the same number.

### AC-12 (US-06) — domain invariant
**Given** an opportunity that the day's assessment already shows to be disqualified on a factual
ground the Owner has stated
**When** the day's comparison work is assembled
**Then** it is excluded from that work, it is recorded as excluded with the specific ground, it
remains retrievable, and it is counted in what the Owner is told was hidden.

### AC-13 (US-01) — cross-context
**Given** the operator asks for a calibration pass
**When** the day's comparison runs
**Then** every opportunity is compared regardless of the factual exclusions, so the effect of those
exclusions can be measured against what they would have hidden.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| **precision@10** | ≥ 6 of the top 10 rated "worth opening" | Weekly Owner rating recorded as Signals |
| Matching cost per Run | < $0.60 at 150 jobs discovered, deep tier | Cost ledger |
| Pre-match pass rate | 35–50% of enriched jobs reach deep tier | `jobhunter.matching.prefiltered` |
| Pre-match regret | 0 filtered jobs would have scored above threshold | Weekly sampler (20 jobs) |
| CV prefix cache hit | > 0 cached read tokens on every batch item after the first | Integration assertion |
| Ranking determinism | identical inputs → identical score, always | Property test |
| Ranking latency | < 5 s for 500 jobs | Benchmark |
| Match parse success | ≥ 97% per batch | `jobhunter.llm.parse_failures` |
| CV leakage | **zero** occurrences in logs, traces, index or notifications | Automated scan suite |
| Re-match after CV change | last 30 days of live jobs, at cheap tier | Integration test |
| Score explainability | 100% of scores decompose to their components | Assertion on every score row |

## 6.1 Security / privacy

- **Data classification:** the CV and the Profile are **confidential personal data** — the only such
  data in the system.
- **Personal data touched:** CV document, extracted text, and the Profile derived from it.
- **AuthZ/AuthN impact:** CV upload, replacement and retrieval are owner-scoped (AC-07). No other
  endpoint exposes CV content, in whole or in excerpt.
- **Abuse cases:**
  - CV text leaking into telemetry → prohibited by construction and verified by an automated scan
    across logs, spans and index documents (AC-06). This is the single most important control in the
    feature.
  - CV text reaching Typesense → the indexer projects `jobs` only; a schema test asserts no CV field exists.
  - Prompt injection in a job description attempting to exfiltrate the CV → the model has no tools and
    no network; its output is schema-bound and written only to typed columns. The worst case is one
    wrong match ([[../../engineering/security]] §5).
  - Malicious CV upload → 5 MB cap, PDF/Markdown/plain text sniffed rather than trusted by extension,
    text extraction in-process with no shell-out.
- **Security review:** **required** before this feature ships — it is the only feature that handles
  personal data, and the leakage-scan suite is its gate.

## 7. Metrics / KPIs

- **precision@10** — baseline established at M4, target ≥ 0.6 and improving after F7.
- **Match cost per job** — target < $0.008 matched (≈ $0.003 per job *discovered*, after the pre-match filter).
- **Suppression rate** — reported, not targeted; a sudden rise means preferences have over-fitted.
- **Interview-probability calibration** — over time, jobs the Owner applied to with a stated 40%
  probability should reach interview roughly 40% of the time. Tracked from M5, informational until
  the sample is meaningful.
- **CV leakage incidents** — target zero, permanently.

## 8. Open questions

- [ ] Is the salary floor a hard filter or a down-weight before F7 has data? — owner: Viacheslav —
  *default: down-weight only; a hard filter requires explicit opt-in.* ([[../../ARCHITECTURE-OPEN-DECISIONS|O5]])
- [ ] Re-match window when the CV changes — owner: Viacheslav — *default: 30 days of live jobs, cheap
  tier.* ([[../../ARCHITECTURE-OPEN-DECISIONS|O10]])
- [ ] Should the ranking weights themselves be Owner-tunable, or fixed until F7? — owner: Viacheslav —
  *default: fixed and documented; F7 tunes the preference component only, never the formula.*
- [ ] Pre-match filter thresholds — how far below the Profile's seniority is a hard exclusion, and at
  what salary confidence does the floor bite? — owner: Viacheslav — *default: two levels, confidence
  ≥ 0.8. Both are configuration; the regret sampler is what tells us if they are wrong.*
  ([[adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]])

## DoD self-check

- [x] Coverage types: happy (01, 11), error (09, 10), authorization (06, 07), domain invariant (02, 03, 05, 12), cross-context (04, 08, 13)
- [x] No implementation tokens in §5
- [x] Every US has ≥1 AC; NFRs measurable; open questions owned with defaults
