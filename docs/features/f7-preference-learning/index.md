---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f7-preference-learning, mvp, jobhunter]
---

# F7 · Preference Learning

> **Feature index (MOC).** Every artifact for this feature, in reading order.

The feature that makes the product improve rather than merely work. Every Ignore, Save, Applied and
interview outcome is evidence; F7 turns that evidence into weights that reorder tomorrow's digest —
and, crucially, into a sentence the digest can say out loud: *"I stopped showing you 34 jobs below
your salary floor."*

Explainability is not decoration here. A learned filter the Owner cannot see is indistinguishable
from a bug, and a filter they can see is the strongest retention mechanism the product has
([[../../DECISION-LOG|D7]]).

## Reading order

1. [[PRD|PRD]] — what may be learned, and what may never be silently applied
2. [[sad|SAD]] — the fitting method, the evidence window, activation
3. [[data-model|Data model]] — `signals`, `preference_models`, `preference_weights`, `suppression_overrides`
4. [[test-plan|Test plan]] — the synthetic-behaviour corpus
5. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 9 tasks

## Architecture decisions

- [[adr/0001-transparent-frequency-weighting|ADR-F7-0001]] — transparent frequency weighting, not a learned ranker
- [[adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]] — no weight without cited evidence

## Milestone

M5 — Compounding. Exit: `precision@10` measurably above the M4 baseline.

## Related

[[../f6-application-tracking/index|← F6]] · [[../f4-cv-matching-ranking/index|F4]] (consumes the weights) ·
[[../../CONTEXT]] invariant 11
