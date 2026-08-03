---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f2-normalization-dedup, mvp, jobhunter]
---

# Task tracker — F2 Normalization & Deduplication

Epic: [[_epic|_epic]]. Zero false merges is the property this feature exists to have; everything else is negotiable.

Each task is one reviewable PR (≤500 LOC), ≤1 day. Owner: Viacheslav (solo).
Estimate legend: **S** ≈ 2 h · **M** ≈ half a day · **L** ≈ a full day.
Status: `pending` → `in_progress` → `in_review` → `done`.

| ID | Task | Layer | Deps | Est | Status |
|---|---|---|---|---|---|
| T01 | [[T01-domain-job\|Domain: Job, Fingerprint, SalaryRange, LocationSet]] | domain | — | M | done |
| T02 | [[T02-title-normalization\|Title normalisation and seniority extraction]] | app | T01 | M | done |
| T03 | [[T03-location-salary-parsing\|Location, remote policy and salary parsing]] | app | T01 | L | done |
| T04 | [[T04-normalization-handler\|Per-provider normalizers and the normalisation handler]] | app | T02, T03 | M | done |
| T05 | [[T05-jobs-persistence\|Migration and repositories for jobs and aliases]] | infra/db | T01 | M | done |
| T06 | [[T06-fingerprint-dedup\|Fingerprint calculation and the deduplication handler]] | app | T04, T05 | L | done |
| T07 | [[T07-technology-tagging\|Technology vocabulary tagging]] | app | T04 | S | done |
| T08 | [[T08-lifecycle-grouping\|Job lifecycle: closure and reopening]] | app | T06 | M | done |
| T09 | [[T09-reprocessing\|Reprocessing and retention]] | app | T06 | M | done |

**9 tasks · 1×S + 6×M + 2×L ≈ 5.25 person-days.**
`NearDuplicateGrouper` (formerly bundled in T08) is **relocated to F5** digest assembly per
[[../adr/0001-conservative-fingerprint|ADR-F2-0001]]; the estimate is unchanged.

## Dependency graph

```mermaid
graph LR
  T01 --> T02 --> T04
  T01 --> T03 --> T04
  T01 --> T05
  T04 --> T06
  T05 --> T06 --> T08
  T04 --> T07
  T06 --> T09
```

## DoR / DoD

- **DoR:** the feature's PRD, SAD, data-model and test-plan are accepted
  ([[../../../IMPLEMENTATION-READINESS|readiness]]); the task's own ACs and ADR links resolve.
- **DoD (every task):** code compiles with zero warnings; the dedup corpus stays at zero false merges; the coverage gate stays green; migrations apply on a clean database; the handler has an idempotency test; the tracker row is updated in the same PR.

See [[../../../IMPLEMENTATION-READINESS]] §4 for the full per-task checklist.
