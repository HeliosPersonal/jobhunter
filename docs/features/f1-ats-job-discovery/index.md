---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f1-ats-job-discovery, mvp, jobhunter]
---

# F1 · ATS Job Discovery

> **Feature index (MOC).** Every artifact for this feature, in reading order.

Turn a list of companies into a continuous stream of raw job postings: maintain the company
registry, detect which ATS each company uses, fetch its board every six hours politely, and store
every payload immutably. F1 owns *acquisition* — it does not interpret what it fetches.

## Reading order

1. [[PRD|PRD]] — what discovery must guarantee, and how failure degrades
2. [[sad|SAD]] — the `IJobSource` port, the five adapters, rate limiting and quarantine
3. [[data-model|Data model]] — `companies`, `ats_bindings`, `job_sources`, `raw_postings`, `source_fetch_log`
4. [[contracts/ats-endpoints|ATS endpoint reference]] — the five providers' actual shapes
5. [[test-plan|Test plan]] — fixture-driven adapter testing with zero network
6. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 12 tasks

## Architecture decisions

- [[../../00-overview/adr/0009-ats-first-no-linkedin|ADR-0009]] — ATS-first, no scraping, tiered expansion
- [[adr/0001-company-registry-seeding|ADR-F1-0001]] — curated seed plus directory expansion
- [[adr/0002-immutable-raw-postings|ADR-F1-0002]] — immutable raw storage with content-hash dedup

## Milestone

M2 — Inventory (with F2). Exit: ≥5 000 live Jobs from ≥4 ATS kinds, dedup rate reported, zero
quarantined sources at steady state.

## Related

[[../f0-platform-foundation/index|← F0]] · [[../f2-normalization-dedup/index|F2 →]] · [[../../CONTEXT]]
