---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f1-ats-job-discovery, mvp, jobhunter]
---

# Epic — F1 ATS Job Discovery

Turn a curated list of companies into a continuous, polite, loss-free stream of raw job postings.
Maintain the registry, detect each company's ATS with evidence, fetch every board six-hourly through
one shared politeness pipeline, store every payload immutably, and isolate every provider's failures
from every other's.

F1 owns **acquisition only**. It never reads meaning out of a payload — that is F2 onward.

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-06, AC-01…AC-12
- SAD: [[../sad|sad]] — `IJobSource`, politeness pipeline, quarantine
- Data model: [[../data-model|data-model]] — five owned tables
- Contracts: [[../contracts/ats-endpoints|ATS endpoint reference]] — the five providers' real shapes
- Test plan: [[../test-plan|test-plan]] — fixture corpus and the 50-company detection set
- ADRs: [[../../../00-overview/adr/0009-ats-first-no-linkedin|0009]],
  [[../adr/0001-company-registry-seeding|F1-0001]], [[../adr/0002-immutable-raw-postings|F1-0002]]

## Scope

**In:** registry and seeding, binding detection and re-detection, the politeness handler, five
`IJobSource` adapters, the six-hourly cycle, immutable raw ingestion with hash dedup, quarantine and
fetch logging.
**Out:** normalisation and dedup into `Job` (F2), any interpretation of a description (F3/F4),
LinkedIn and aggregators (ADR-0009), company enrichment (F3/F8).

## Module scope

`Domain/Companies`, `Domain/Jobs/RawPosting`, `Application/Discovery`, `JobHunter.Scrapers` (all five
adapters plus fixtures), `Infrastructure/Http/PolitenessHandler`, `Infrastructure/Caching`,
`Infrastructure/Persistence` (five tables), `tools/seed/`.

## Handoff interfaces

| Produces | Consumer |
|---|---|
| `RawPostingIngested` | F2 normalisation |
| `SourceQuarantined` | Telegram notifier, digest footer |
| `JobClosed` (from liveness) | F2, F6, F9 |
| `companies` table | F3 (stage), F8 (research targets) |
| `raw_postings` table | F2 (read-only) |

## Tasks

See [[tracker|tracker]]. 13 tasks, ≈ **7.5** person-days (T13 adds the `JobClosed` closure sweep).

## Definition of Done (epic)

- AC-01…AC-12 covered by passing tests, all hermetic except the weekly contract suite.
- ≥ 5 000 raw postings ingested from ≥ 4 ATS kinds against the seeded registry.
- Binding detection ≥ 95% accurate on the 50-company labelled set.
- Unchanged-content re-store rate is 0%; the unchanged ratio metric sits near 90%.
- No type in `JobHunter.Scrapers` can construct its own `HttpClient` (QG-2, asserted).
- `raw_postings.payload` has no update path (QG-3, asserted).
- Contributes to milestone M2 in [[../../../BACKLOG|BACKLOG]] §1.
