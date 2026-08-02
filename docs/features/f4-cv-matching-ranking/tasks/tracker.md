---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f4-cv-matching-ranking, mvp, jobhunter]
---

# Task tracker — F4 CV Matching & Ranking

Epic: [[_epic|_epic]]. The only feature that handles personal data. A security review is required before it ships.

Each task is one reviewable PR (≤500 LOC), ≤1 day. Owner: Viacheslav (solo).
Estimate legend: **S** ≈ 2 h · **M** ≈ half a day · **L** ≈ a full day.
Status: `pending` → `in_progress` → `in_review` → `done`.

| ID | Task | Layer | Deps | Est | Status |
|---|---|---|---|---|---|
| T01 | [[T01-domain-profile-match\|Domain: Profile, CvVersion, Match, Score]] | domain | — | M | pending |
| T02 | [[T02-matching-persistence\|Migration and repositories for profiles, CVs, matches, scores]] | infra/db | T01 | M | pending |
| T03 | [[T03-cv-upload-versioning\|CV upload, text extraction and versioning]] | app | T02 | M | pending |
| T04 | [[T04-match-prompt-schema\|Match prompt, schema and parser]] | claude | T01 | L | pending |
| T05 | [[T05-matching-submit\|Matching submission through the F3 Run machinery]] | app | T04, T02 | M | pending |
| T06 | [[T06-match-result-processing\|Match result processing]] | app | T05 | M | pending |
| T07 | [[T07-score-calculator\|ScoreCalculator]] | app | T01 | M | pending |
| T08 | [[T08-ranking-suppression\|Ranking handler and suppression]] | app | T07, T06 | M | pending |
| T09 | [[T09-cv-restaling\|CV activation, re-staling and re-match scheduling]] | app | T03, T06 | M | pending |
| T10 | [[T10-leakage-suite\|CV leakage scan suite]] | tests | T06, T08 | L | pending |
| T11 | [[T11-golden-ranking\|Golden ranking set and precision tracking]] | tests | T08 | L | pending |
| T12 | [[T12-pre-match-filter\|Pre-match filter]] | app | T05 | M | pending |
| T13 | [[T13-cv-prompt-caching\|CV prompt caching and regret sampler]] | claude/app | T04, T12 | M | pending |

**13 tasks · 10×M + 3×L ≈ 8 person-days.**

## Dependency graph

```mermaid
graph LR
  T01 --> T02 --> T03
  T01 --> T04 --> T05 --> T06
  T02 --> T05
  T01 --> T07 --> T08
  T06 --> T08
  T03 --> T09
  T06 --> T09
  T05 --> T12 --> T13
  T04 --> T13
  T06 --> T10
  T08 --> T10
  T08 --> T11
```

## DoR / DoD

- **DoR:** the feature's PRD, SAD, data-model and test-plan are accepted
  ([[../../../IMPLEMENTATION-READINESS|readiness]]); the task's own ACs and ADR links resolve.
- **DoD (every task):** code compiles with zero warnings; the CV leakage suite is green with no allowlist; the golden ranking set passes; every score reconciles from its components; the coverage gate stays green; the tracker row is updated in the same PR.

See [[../../../IMPLEMENTATION-READINESS]] §4 for the full per-task checklist.
