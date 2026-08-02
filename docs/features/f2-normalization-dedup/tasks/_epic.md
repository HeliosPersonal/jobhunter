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

# Epic — F2 Normalization & Deduplication

Turn raw provider payloads into one canonical `Job` per real opening: comparable title, seniority,
locations, remote policy, employment type and salary; a conservative fingerprint that never merges
two distinct openings; a provenance trail; and a lifecycle that closes a job when its posting
disappears.

The property this feature exists to have is **zero false merges**. A duplicate costs one tap; a false
merge costs an opportunity the Owner never learns about.

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-06, AC-01…AC-11
- SAD: [[../sad|sad]] — two-stage pipeline, fingerprint, aliases, lifecycle
- Data model: [[../data-model|data-model]] — `jobs`, `job_aliases`, `job_technologies`
- Test plan: [[../test-plan|test-plan]] — the labelled dedup corpus
- ADR: [[../adr/0001-conservative-fingerprint|F2-0001]]
- Upstream feature: [[../../f1-ats-job-discovery/index|F1]] (produces `RawPostingIngested`)

## Scope

**In:** provider-specific extraction, shared normalisation, vocabulary technology tagging,
fingerprinting, alias recording, closure and reopening, reprocessing and retention.
**Out:** enrichment and matching (F3/F4), model-based technology extraction (F3), search indexing
(F9), translation (backlog).

## Module scope

`Domain/Jobs`, `Application/Normalization`, `Application/Deduplication`, `Infrastructure/Persistence`
(three tables), `tools/vocabulary/technologies.yaml`.

## Handoff interfaces

| Produces | Consumer |
|---|---|
| `JobDiscovered` | F3 enrichment, F9 indexing |
| `JobDuplicateDetected` | metrics |
| `JobClosed` | F6 tracking, F9 index removal |
| `jobs` table | F3, F4, F5, F6, F7, F9 — all read-only |

## Tasks

See [[tracker|tracker]]. 9 tasks, ≈ **5.25** person-days.

## Definition of Done (epic)

- AC-01…AC-11 covered by passing tests.
- **Zero false merges** on a ≥ 200-pair labelled corpus; false splits ≤ 5%.
- Seniority extraction ≥ 95% on 200 labelled titles; location coverage ≥ 90%.
- Fingerprints stable against 50 frozen expectations under three cultures.
- Reprocessing runs over stored payloads at ≥ 5 000/min with zero network.
- Deduplication rate reported and sitting in the 10–20% band.
- Completes milestone M2 in [[../../../BACKLOG|BACKLOG]] §1.
