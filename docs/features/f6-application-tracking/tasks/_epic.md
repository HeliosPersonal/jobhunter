---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f6-application-tracking, mvp, jobhunter]
---

# Epic — F6 Application Tracking

Track every opportunity the Owner engages with through its full lifecycle, with a complete immutable
history, one-tap updates, and reminders when something has gone quiet.

Two things make this more than a to-do list. First, the pipeline maintains itself from actions the
Owner is already taking in the digest — which is the difference between a tracker that stays current
and the spreadsheet everyone abandons by week three. Second, **an application that reaches `Interview`
or `Offer` is the strongest preference evidence the system will ever get**, because it survived a real
filter rather than two seconds of attention.

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-07, AC-01…AC-10
- SAD: [[../sad|sad]] — transition rules, reminders, the outcome loop
- Data model: [[../data-model|data-model]] — `applications`, `application_transitions`, `application_notes`
- Contract: [[../contracts/application-api|Application API]] — endpoints and the transition matrix
- Test plan: [[../test-plan|test-plan]] — the transition matrix suite
- ADR: [[../adr/0001-permissive-transitions-with-history|F6-0001]]
- Upstream feature: [[../../f5-daily-digest-telegram/index|F5]] (produces `OwnerActionRecorded`)

## Scope

**In:** the application aggregate and transition rules, lazy creation and advancement from digest
actions, the pipeline and history views, notes, the reminder sweep, weighted outcome signals, the
owner-scoped API.
**Out:** applying on the Owner's behalf (never — [[../../../CONTEXT]] invariant 7), email and calendar
integration (backlog), interview preparation (backlog), preference fitting (F7).

## Module scope

`Domain/Applications`, `Application/Applications`, `Infrastructure/Persistence` (three tables),
`JobHunter.Telegram/Handlers` (pipeline, notes, status callbacks), five endpoints on `JobHunter.Api`.

## Handoff interfaces

| Produces | Consumer |
|---|---|
| `ApplicationStatusChanged` | F7 signal capture, F9 index update |
| Weighted `signals` rows | F7 (which owns the schema) |
| `applications` table | F5 `/pipeline`, F9 search filtering |

## Tasks

See [[tracker|tracker]]. 9 tasks, ≈ 5 person-days.

## Definition of Done (epic)

- AC-01…AC-10 covered by passing tests.
- **The transition matrix suite covers every status pair**, not a selected subset.
- `application_transitions` has no update and no delete path, asserted by an architecture test.
- A stale application produces exactly one reminder across seven simulated days.
- A closed posting marks the application without fabricating a rejection.
- Terminal outcomes produce weighted signals usable by F7.
- Pipeline view renders 200 applications in under 500 ms.
- Contributes to milestone M5 in [[../../../BACKLOG|BACKLOG]] §1.
