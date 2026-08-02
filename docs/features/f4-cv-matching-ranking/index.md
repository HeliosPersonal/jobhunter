---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f4-cv-matching-ranking, mvp, jobhunter]
---

# F4 · CV Matching & Ranking

> **Feature index (MOC).** Every artifact for this feature, in reading order.

The stage where the product's actual promise is kept: given the Owner's CV and a job's enrichment,
decide how well they fit, why, what is missing, and what the realistic interview probability is —
then turn that judgement plus preference weights plus freshness into the one number the digest
orders by.

This is also the only place in the system where the CV crosses a boundary. That boundary is drawn
here deliberately and nowhere else.

## Reading order

1. [[PRD|PRD]] — what a match must say, and what a Score must never be
2. [[sad|SAD]] — profile and CV versioning, the matching batch, the ranking formula
3. [[data-model|Data model]] — `profiles`, `cv_versions`, `matches`, `scores`
4. [[contracts/match-schema|Match output contract]] — schema, prompt, CV handling rules
5. [[test-plan|Test plan]] — the golden ranking set and the CV-leakage suite
6. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 13 tasks

## Architecture decisions

- [[adr/0001-explainable-linear-scoring|ADR-F4-0001]] — a transparent linear score, not a learned one
- [[adr/0002-cv-versioning-and-restaling|ADR-F4-0002]] — CV versions, and what happens to old matches
- [[adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]] — pre-match filter and CV prompt caching (the cost decision)

## Milestone

M3 — Intelligence (with F3).

## Related

[[../f3-claude-batch-enrichment/index|← F3]] · [[../f5-daily-digest-telegram/index|F5 →]] ·
[[../f7-preference-learning/index|F7]] (supplies the weights) · [[../../CONTEXT]] invariant 4
