---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f6-application-tracking, mvp, jobhunter]
---

# F6 · Application Tracking

> **Feature index (MOC).** Every artifact for this feature, in reading order.

The hiring pipeline as a first-class thing: `New → Saved → Applied → Interview → Rejected | Offer`,
with a timeline of how each application got where it is, notes, and reminders when something has been
sitting too long.

F6 also closes an important loop — an application that reaches `Interview` or `Offer` is the strongest
preference signal the system will ever get, far stronger than a tap on a card.

## Reading order

1. [[PRD|PRD]] — what a status means and which transitions are legal
2. [[sad|SAD]] — the transition table, reminders, the outcome feedback loop
3. [[data-model|Data model]] — `applications`, `application_transitions`, `application_notes`
4. [[contracts/application-api|Application API]] — the operator endpoints
5. [[test-plan|Test plan]] — the transition matrix
6. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 9 tasks

## Architecture decisions

- [[adr/0001-permissive-transitions-with-history|ADR-F6-0001]] — permissive transitions, complete history

## Milestone

M5 — Compounding.

## Related

[[../f5-daily-digest-telegram/index|← F5]] · [[../f7-preference-learning/index|F7 →]] · [[../../CONTEXT]] §1
