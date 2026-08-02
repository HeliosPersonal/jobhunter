---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f10-telegram-commands, mvp, jobhunter]
---

# Epic — F10 Telegram Command Interface

Twenty commands that make the whole system reachable from the chat: search the corpus, work the
pipeline, read a company dossier, inspect and correct what the system learned, and run the common
recovery steps — all in the digest's card language.

F10 adds **no new capability**. Every command answers from a query service another feature already
owns, and every write goes through that feature's own service. It is a surface, and its risk is
surface-shaped: twenty commands drift apart unless something holds them together. That something is
the registry and its conformance suite.

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-09, AC-01…AC-12
- SAD: [[../sad|sad]] — registry, dispatch, conversation state, confirmation
- Data model: [[../data-model|data-model]] — `command_invocations`; conversation state in Redis
- Contract: [[../contracts/command-catalogue|command catalogue]] — all twenty commands
- Test plan: [[../test-plan|test-plan]] — the catalogue-conformance suite
- ADRs: [[../adr/0001-declarative-command-registry|F10-0001]], [[../adr/0002-no-conversational-fallback|F10-0002]]
- Reused: [[../../f5-daily-digest-telegram/index|F5]] host, formatter, escaper, allowlist

## Scope

**In:** the command registry and argument parser, dispatch with allowlist/scope/rate-limiting,
conversation state and cancellation, the confirmation flow, all twenty command handlers, menu
generation, grouped help, typo suggestions, the conformance suite.
**Out:** the read models (F5–F9), the digest and its delivery (F5), any capability not already
exposed by an existing service, and anything conversational
([[../adr/0002-no-conversational-fallback|ADR-F10-0002]]).

## Module scope

`Domain/Commands`, `Application/Commands`, `JobHunter.Telegram/Commands` (dispatcher, handlers, menu
synchroniser), `Infrastructure/Persistence` (one table), Redis keys for conversation state and
confirmation nonces.

## Handoff interfaces

| Consumes | From |
|---|---|
| Card formatter, escaper, allowlist, fake notifier | F5 |
| Application and note services | F6 |
| Preference model, overrides, suppressions | F7 |
| Company dossiers | F8 |
| Search and read models | F9 |

Produces nothing new — a state-changing command writes through the owning feature, which publishes
its own event as usual.

## Tasks

See [[tracker|tracker]]. 10 tasks, ≈ 6 person-days.

## Definition of Done (epic)

- AC-01…AC-12 covered by passing tests.
- **The conformance suite is green in both directions** — no command built without documentation, and
  none documented without being built.
- Every command declares a scope; every state-changing command requires a confirmation naming its
  effect. Both assertions have proven-red fixtures.
- **`/cv` exposes no CV content** — the F4 sentinel scan is extended to cover the command path.
- The generated client menu matches the registry exactly.
- Every command has a committed rendering snapshot in the shared F5 corpus.
- Read commands answer under 2 s p95; search under 3 s.
- The runbooks reference the operations commands alongside the API endpoints.
- Contributes to milestone M5 in [[../../../BACKLOG|BACKLOG]] §1.
