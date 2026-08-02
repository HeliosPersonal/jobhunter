---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f2-normalization-dedup, mvp, jobhunter]
---

# F2 · Normalization & Deduplication

> **Feature index (MOC).** Every artifact for this feature, in reading order.

Turn raw provider payloads into the canonical `Job`: one row per real vacancy, with a normalised
title, a parsed location set, a resolved remote policy, a structured salary where one is published,
and a lifecycle that knows when the posting disappeared. Then make sure the same vacancy appearing on
three boards is one `Job`, not three.

## Reading order

1. [[PRD|PRD]] — what "one job" means and what may never be merged
2. [[sad|SAD]] — the normalisation pipeline, the fingerprint, the alias table
3. [[data-model|Data model]] — `jobs`, `job_aliases`, `job_technologies`
4. [[test-plan|Test plan]] — the labelled dedup corpus
5. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 9 tasks

## Architecture decisions

- [[adr/0001-conservative-fingerprint|ADR-F2-0001]] — a conservative exact fingerprint, aliases not merges

## Milestone

M2 — Inventory (with F1).

## Related

[[../f1-ats-job-discovery/index|← F1]] · [[../f3-claude-batch-enrichment/index|F3 →]] · [[../../CONTEXT]] invariant 2
