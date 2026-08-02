---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f3-claude-batch-enrichment, mvp, jobhunter]
---

# F3 · Claude Batch Enrichment

> **Feature index (MOC).** Every artifact for this feature, in reading order.

The engine room. F3 introduces the **Run** — the durable, resumable state machine that owns a day's
intelligence work — and the **Batch** lifecycle that survives hours of asynchronous waiting and any
number of process restarts. It uses that machinery to produce an `Enrichment` for every new job:
salary estimate, remote and contractor signals, timezone band, AI-usage level, technologies, company
stage and reasons.

Everything expensive in this system passes through the abstractions F3 builds. F4, F5 and F8 all
submit batches through the same client, spend against the same ledger and resume through the same
poller.

## Reading order

1. [[PRD|PRD]] — what a Run guarantees, and what happens when it cannot
2. [[sad|SAD]] — the Run state machine, the Batch lifecycle, cost enforcement
3. [[data-model|Data model]] — `runs`, `batches`, `batch_items`, `cost_ledger_entries`, `enrichments`
4. [[contracts/enrichment-schema|Enrichment output contract]] — the schema and the prompt
5. [[test-plan|Test plan]] — fixtures, the crash matrix, the cost-ceiling assertions
6. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 14 tasks

## Architecture decisions

- [[../../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]] — Batch API, two tiers
- [[../../00-overview/adr/0006-structured-output-contract|ADR-0006]] — schema-bound output, tolerant parsing
- [[adr/0001-run-as-resumable-state-machine|ADR-F3-0001]] — the Run as a durable aggregate
- [[adr/0002-pre-submission-cost-ceiling|ADR-F3-0002]] — estimate before spending, not after

## Milestone

M3 — Intelligence (with F4). Exit: every new job carries an Enrichment and a Match; the enrichment
**stage** costs ≈$0.43 and a full Run ≈$1.03, under the $2.00 ceiling; a killed worker resumes without
duplicate spend.

## Related

[[../f2-normalization-dedup/index|← F2]] · [[../f4-cv-matching-ranking/index|F4 →]] · [[../../CONTEXT]] invariants 3 and 6
