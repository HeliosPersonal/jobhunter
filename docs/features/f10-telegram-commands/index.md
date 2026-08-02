---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f10-telegram-commands, mvp, jobhunter]
---

# F10 · Telegram Command Interface

> **Feature index (MOC).** Every artifact for this feature, in reading order.

The 07:00 digest is the product's heartbeat, but it is a *push*. F10 is the **pull** side: 22
commands that make the whole system reachable from the one place the Owner already is — search the
corpus, work the pipeline, read a company dossier, inspect what the system learned about them, check
what last night's Run cost, and trigger a re-run when something looks wrong.

The design constraint is the same one that shaped F5: one thumb, half awake. Every command answers in
the **same card language as the digest**, so there is one visual grammar across the product rather
than a bot that renders each feature differently.

## Reading order

1. [[PRD|PRD]] — what the command surface must do, and what it must never become
2. [[sad|SAD]] — the registry, dispatch, conversation state, authorization
3. [[data-model|Data model]] — `command_invocations`, `conversation_states`
4. [[contracts/command-catalogue|Command catalogue]] — all 22 commands, arguments, output shapes
5. [[test-plan|Test plan]] — the catalogue-conformance suite
6. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 10 tasks

## Architecture decisions

- [[adr/0001-declarative-command-registry|ADR-F10-0001]] — a declarative registry, not a switch statement
- [[adr/0002-no-conversational-fallback|ADR-F10-0002]] — no LLM in the command path

## Milestone

M5 — Compounding. `/start`, `/help` and `/digest` ship earlier with
[[../f5-daily-digest-telegram/tasks/T11-command-set|F5 T11]]; F10 delivers the full catalogue and
the registry that makes it safe to grow.

## Related

[[../f5-daily-digest-telegram/index|← F5]] (rendering and delivery) ·
[[../f6-application-tracking/index|F6]] · [[../f7-preference-learning/index|F7]] ·
[[../f8-company-research-agent/index|F8]] · [[../f9-search-and-api/index|F9]] (the read models)
