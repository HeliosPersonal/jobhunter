---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f7-preference-learning, mvp, jobhunter]
---

# Task tracker — F7 Preference Learning

Epic: [[_epic|_epic]]. Explainability is a hard requirement here: a filter the Owner cannot see is indistinguishable from a bug.

Each task is one reviewable PR (≤500 LOC), ≤1 day. Owner: Viacheslav (solo).
Estimate legend: **S** ≈ 2 h · **M** ≈ half a day · **L** ≈ a full day.
Status: `pending` → `in_progress` → `in_review` → `done`.

| ID | Task | Layer | Deps | Est | Status |
|---|---|---|---|---|---|
| T01 | [[T01-domain-preferences\|Domain: Signal, PreferenceModel, PreferenceWeight]] | domain | — | M | pending |
| T02 | [[T02-preference-persistence\|Migration and repositories]] | infra/db | T01 | S | pending |
| T03 | [[T03-signal-capture\|Signal capture verification]] | app | T02 | S | pending |
| T04 | [[T04-weight-fitter\|WeightFitter]] | app | T01 | L | pending |
| T05 | [[T05-preference-learner\|PreferenceLearner and weekly refit]] | app | T04, T02 | M | pending |
| T06 | [[T06-preference-component\|Preference component and precedence]] | app | T05 | M | pending |
| T07 | [[T07-suppression-floor\|Suppression evaluation and the card floor]] | app | T06 | M | pending |
| T08 | [[T08-explainability-overrides\|Explainability view and Owner overrides]] | app/api | T07 | M | pending |
| T09 | [[T09-corpus-and-metrics\|Synthetic corpus, property suite and precision tracking]] | tests | T04, T07 | L | pending |

**9 tasks · 2×S + 5×M + 2×L ≈ 5 person-days.**

## Dependency graph

```mermaid
graph LR
  T01 --> T02 --> T03
  T01 --> T04 --> T05 --> T06 --> T07 --> T08
  T02 --> T05
  T04 --> T09
  T07 --> T09
```

## DoR / DoD

- **DoR:** the feature's PRD, SAD, data-model and test-plan are accepted
  ([[../../../IMPLEMENTATION-READINESS|readiness]]); the task's own ACs and ADR links resolve.
- **DoD (every task):** code compiles with zero warnings; all nine synthetic profiles pass including the indifferent one; every weight cites at least three signals; every suppression records a reason; the coverage gate stays green; the tracker row is updated in the same PR.

See [[../../../IMPLEMENTATION-READINESS]] §4 for the full per-task checklist.
