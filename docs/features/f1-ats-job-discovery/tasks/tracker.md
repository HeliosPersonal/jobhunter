---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-03"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f1-ats-job-discovery, mvp, jobhunter]
---

# Task tracker — F1 ATS Job Discovery

Epic: [[_epic|_epic]]. Acquisition only — F1 never interprets what it fetches.

> Tasks T14–T15 added from the [[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] (career-goal alignment).

Each task is one reviewable PR (≤500 LOC), ≤1 day. Owner: Viacheslav (solo).
Estimate legend: **S** ≈ 2 h · **M** ≈ half a day · **L** ≈ a full day.
Status: `pending` → `in_progress` → `in_review` → `done`.

| ID | Task | Layer | Deps | Est | Status |
|---|---|---|---|---|---|
| T01 | [[T01-domain-company-binding\|Domain: Company, AtsBinding, CanonicalDomain]] | domain | — | M | done |
| T02 | [[T02-registry-persistence\|Migration and repositories for registry tables]] | infra/db | T01 | M | done |
| T03 | [[T03-registry-seeding\|Company registry seeding and expansion]] | app | T02 | M | done |
| T04 | [[T04-politeness-handler\|PolitenessHandler: rate limit, robots, SSRF, user-agent]] | infra/http | — | L | done |
| T05 | [[T05-ijobsource-greenhouse\|IJobSource port and Greenhouse adapter]] | scrapers | T04 | M | done |
| T06 | [[T06-lever-ashby-workable\|Lever, Ashby and Workable adapters]] | scrapers | T05 | L | done |
| T07 | [[T07-jsonld-careers-adapter\|JSON-LD career-page adapter (Tier 2)]] | scrapers | T05 | M | done |
| T08 | [[T08-binding-detection\|ATS binding detection]] | app | T06 | L | done |
| T09 | [[T09-binding-redetection\|Binding re-detection and ATS migration]] | app | T08 | M | done |
| T10 | [[T10-discovery-cycle\|Discovery cycle handler and fan-out]] | app | T02, T05 | M | done |
| T11 | [[T11-raw-ingestion-dedup\|Raw posting ingestion with content-hash dedup]] | app | T10 | M | done |
| T12 | [[T12-quarantine-logging\|Quarantine, fetch logging and degraded reporting]] | app | T11 | M | done |
| T13 | [[T13-closure-sweep\|Closure sweep: emit JobClosed for postings gone from their board]] | app | T11 | S | done |
| T14 | [[T14-ai-devtools-company-universe\|Grow the company registry with pure-play AI / dev-tools / infra employers]] | app | T03 | L | pending |
| T15 | [[T15-comp-band-remote-tagging\|Tag companies by comp band and remote-from-EMEA posture]] | app | T03 | M | pending |

**13 tasks · 1×S + 9×M + 3×L ≈ 7.5 person-days** (T13 is the small closure sweep).

**Career-alignment tuning tasks (T14–T15): 1×L + 1×M ≈ 1.5 person-days.** Added from the
[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] (TUNE-09, TUNE-10);
they grow `companies.yaml` toward ~300 with pure-play AI / dev-tools / infra employers and tag companies
by comp band + remote-from-EMEA posture to bias discovery and the digest toward the target band.

## Dependency graph

```mermaid
graph LR
  T01 --> T02 --> T03
  T02 --> T10
  T04 --> T05 --> T06 --> T08 --> T09
  T05 --> T07
  T05 --> T10 --> T11 --> T12
  T11 --> T13
  T03 --> T14
  T03 --> T15
```

## DoR / DoD

- **DoR:** the feature's PRD, SAD, data-model and test-plan are accepted
  ([[../../../IMPLEMENTATION-READINESS|readiness]]); the task's own ACs and ADR links resolve.
- **DoD (every task):** code compiles with zero warnings; adapter changes ship with fixtures; the coverage gate stays green; migrations apply on a clean database; the handler has an idempotency test; the tracker row is updated in the same PR.

See [[../../../IMPLEMENTATION-READINESS]] §4 for the full per-task checklist.
