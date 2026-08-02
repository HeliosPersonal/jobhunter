---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f4-cv-matching-ranking, mvp, jobhunter]
---

# Epic — F4 CV Matching & Ranking

Compare every enriched job against the Owner's CV, produce a fit judgement with reasons, missing
skills and a realistic interview probability — then combine that judgement with learned preferences
and freshness into the single number the digest orders by.

Two properties define this feature:

1. **The CV crosses exactly one boundary.** This is the only stage that touches personal data, and
   the leakage suite is the gate that proves it.
2. **Every number is explainable.** The model judges; the ordering is arithmetic we can show, test
   and tune without a prompt change.

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-07, AC-01…AC-13
- SAD: [[../sad|sad]] — matching stage, ranking formula, CV boundary
- Data model: [[../data-model|data-model]] — `profiles`, `cv_versions`, `matches`, `scores`
- Contract: [[../contracts/match-schema|match schema]] — schema, prompt, CV handling rules, formula
- Test plan: [[../test-plan|test-plan]] — the leakage suite and the golden ranking set
- ADRs: [[../adr/0001-explainable-linear-scoring|F4-0001]], [[../adr/0002-cv-versioning-and-restaling|F4-0002]], [[../adr/0003-pre-match-filter-and-cv-caching|F4-0003]]
- Reused unchanged: [[../../f3-claude-batch-enrichment/sad|F3]] Run, Batch, poller, cost machinery

## Scope

**In:** profile and CV ingestion, text extraction, versioning and re-staling; the pre-match filter and
CV prompt caching; the matching batch, prompt and schema; the ranking formula, suppression and score
persistence; `precision@10` measurement.
**Out:** enrichment (F3), preference fitting (F7 — F4 consumes the active model), digest assembly
(F5), search (F9), CV advice (backlog).

## Module scope

`Domain/Profiles`, `Domain/Intelligence/{Match,Score}`, `Application/Matching`, `Application/Ranking`,
`JobHunter.Claude/Prompts/MatchPrompt.cs`, `Infrastructure/Cv`, `Infrastructure/Persistence`
(four tables), one owner-scoped API endpoint group.

## Handoff interfaces

| Produces | Consumer |
|---|---|
| `MatchingCompleted` | ranking |
| `RankingCompleted` | F5 reporting, F8 research, F9 indexing |
| `CvVersionActivated` | the re-match scheduler |
| `scores` table | F5 digest, F7 suppression feedback, F9 ordering |
| `matches` table | F5, F6, F9 (read-only) |

## Tasks

See [[tracker|tracker]]. 13 tasks · 10×M + 3×L ≈ 8.0 person-days.

## Definition of Done (epic)

- AC-01…AC-13 covered by passing tests.
- **The CV leakage suite is green** — zero sentinel occurrences in any artifact, at any log level,
  including forced-failure paths. No allowlist.
- **The golden ranking set passes**, including all ten difficult cases.
- Every persisted score reconciles from its stored components (100%, not sampled).
- Ranking is deterministic over 10 000 generated inputs under three cultures.
- Matching cost under **$0.60** at 150 jobs discovered, with the pre-match filter passing 35–50%
  and the CV prefix cache hitting on every batch item after the first.
- Pre-match regret is zero: no excluded job would have scored above the presentation threshold.
- A `precision@10` baseline is captured so F7's improvement is measurable.
- **Security review completed** before ship ([[../PRD|PRD]] §6.1).
- Completes milestone M3 in [[../../../BACKLOG|BACKLOG]] §1.
