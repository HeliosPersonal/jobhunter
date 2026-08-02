---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f10-telegram-commands, mvp, jobhunter]
---

# PRD — f10-telegram-commands

> **Inputs:** [[../f5-daily-digest-telegram/PRD|F5 PRD]] · [[../../CONTEXT]] · [[../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]]

## 1. Context

By M5 the system holds a corpus of several thousand analysed jobs, an application pipeline, a set of
learned preferences, company dossiers and a cost ledger. Exactly one of those is reachable from
Telegram: the morning digest. Everything else requires `curl` and a Keycloak token
([[../f9-search-and-api/index|F9]]) — which is fine for the reviewer audience and useless at 23:40
on a phone.

That gap shows up as small, constant friction. *Did I already apply to this one? What did the system
say about Stripe? Why did I stop seeing contract roles? Did last night's run actually finish?* Each
is a ten-second question that currently costs a laptop.

The commands are also where the system becomes **inspectable**. [[../../CONTEXT]] invariant 11 says
suppression must be visible and [[../../DECISION-LOG|D7]] says visibility is what makes ignoring feel
productive — but a footer count is only half of that promise. `/hidden` and `/prefs` are the other
half: the Owner can see exactly what was filtered and *why*, and switch a preference off if it is
wrong.

The thing to guard against is scope: a bot with 22 commands drifts toward being a chat interface,
and a chat interface sets an expectation this product does not meet
([[adr/0002-no-conversational-fallback|ADR-F10-0002]]).

## 2. Goals

- Make every read model the system holds reachable from Telegram in one command.
- Let the Owner work the application pipeline without leaving the chat.
- Expose what the system learned about them, and let them correct it.
- Give the operator enough to answer "is it healthy and what did it cost" without a laptop.
- Keep one visual grammar: every command answers in the digest's card language.
- Make adding a command safe — a new command cannot ship without authorization or documentation.

## 3. Non-goals

- A conversational assistant. The bot answers a fixed catalogue; it does not chat
  ([[adr/0002-no-conversational-fallback|ADR-F10-0002]]).
- Any capability that does not already exist behind an API — F10 is a *surface*, not new behaviour.
- Multi-user support, inline mode, or group chats. One Owner, one private chat.
- Destructive operations without confirmation. Nothing irreversible happens on a single tap.
- Rich media. Text and inline keyboards only.

## 4. User stories

### US-01: Find something I remember seeing
**As the** Owner **I want** to search everything the system has seen, from the chat
**so that** I can find a role I did not save without opening a laptop.

### US-02: Work the pipeline from my phone
**As the** Owner **I want** to see and update my applications from the chat **so that** keeping the
pipeline current never requires a desk.

### US-03: Read up on a company
**As the** Owner **I want** a company's dossier on request **so that** I can prepare on the way to
a conversation.

### US-04: See what the system decided about me
**As the** Owner **I want** to inspect the preferences it learned and the opportunities it hid
**so that** I can tell a working filter from a wrong one.

### US-05: Correct a wrong preference immediately
**As the** Owner **I want** to switch off a learned preference from the chat **so that** a bad
inference costs me one message, not a week of narrowed results.

### US-06: Know the system is healthy
**As the** operator **I want** last night's outcome, spend and source health on request
**so that** I can confirm things are working without opening a dashboard.

### US-07: Recover from a bad night
**As the** operator **I want** to re-run or re-deliver from the chat **so that** the most common
recovery does not need a terminal.

### US-08: Discover what I can do
**As the** Owner **I want** the command list to be visible in the client and to explain itself
**so that** I do not have to remember 22 commands.

### US-09: Not be trapped mid-command
**As the** Owner **I want** to abandon a command that is waiting on me **so that** a half-finished
interaction never blocks the next one.

## 5. Acceptance criteria

### AC-01 (US-08) — happy path
**Given** the Owner opens the chat
**When** they view the client's command list
**Then** every command the system actually supports is listed with a one-line description, and no
command is listed that does not exist.

### AC-02 (US-01) — happy path
**Given** an indexed corpus
**When** the Owner issues a search with terms and optional narrowing
**Then** matching opportunities are returned in the same scannable form as the daily digest, with the
count found and a way to see more.

### AC-03 (US-02) — cross-context
**Given** applications in several stages
**When** the Owner requests the pipeline
**Then** each is presented under its stage, most recently active first, and each can be advanced
without typing another command.

### AC-04 (US-04) — domain invariant
**Given** opportunities hidden by a learned preference
**When** the Owner asks what was hidden
**Then** each is listed with the specific reason and the evidence behind it, and remains retrievable.

### AC-05 (US-05) — happy path
**Given** a learned preference the Owner disagrees with
**When** they switch it off from the chat
**Then** it stops affecting ordering from the next ranking onward, and they are told when that takes
effect.

### AC-06 (US-06) — happy path
**Given** a completed or failed overnight run
**When** the operator asks for status
**Then** they receive its outcome, what it cost against the ceiling, how many opportunities it
produced, and any degraded sources.

### AC-07 (US-07) — authorization
**Given** a command that changes system state
**When** it is issued
**Then** it requires an explicit confirmation step before taking effect, and the confirmation
identifies exactly what will happen.

### AC-08 (US-09) — error path
**Given** a command waiting for further input
**When** the Owner abandons it, or does nothing for the timeout period
**Then** the command is cancelled, they are told, and the next message is treated normally.

### AC-09 (US-08) — error path
**Given** an unrecognised or malformed command
**When** it is received
**Then** the Owner is told what was wrong and offered the closest valid alternative, without a
generic failure.

### AC-10 (US-01) — authorization
**Given** any command from anyone other than the Owner
**When** it is received
**Then** it is discarded before any handler runs and the attempt is recorded.

### AC-11 (US-03) — error path
**Given** a request about a company the system does not know
**When** it is processed
**Then** the Owner is told plainly and offered the option to add it, rather than receiving an empty
result.

### AC-12 (US-08) — domain invariant
**Given** a command exists in the system
**When** the command surface is inspected
**Then** that command declares who may run it and whether it changes state; a command that declares
neither cannot be reached.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Response latency | < 2 s p95 for read commands | `jobhunter.command.duration` |
| Search latency | < 3 s p95 including render | Benchmark |
| Command menu accuracy | 100% of registered commands listed, 0 phantom entries | Registry-to-menu conformance test |
| Conversation timeout | 5 min, then auto-cancel | Configuration, asserted |
| Unknown-command suggestion | correct suggestion for any single-character typo | Fixture set |
| Authorization coverage | 100% of commands declare a scope | Registry convention test |
| Rate limiting | ≤ 20 commands/minute, then throttle with a message | Asserted |

## 6.1 Security / privacy

- **Data classification:** commands expose confidential data — pipeline, preferences, costs. **No CV
  content is reachable through any command**, including `/cv`, which shows metadata only.
- **Personal data touched:** application notes and preference evidence.
- **AuthZ/AuthN impact:** the chat-id allowlist gates everything before dispatch (AC-10). State-changing
  commands additionally require an explicit confirmation tap (AC-07).
- **Abuse cases:**
  - An unknown chat probing the command surface → dropped before dispatch; the attempt is logged with
    the chat id and never produces output that reveals the catalogue.
  - A destructive command issued by mistake → confirmation step names the exact effect; the callback
    payload carries a nonce so a stale confirmation cannot be replayed.
  - Argument injection into a search or filter → arguments are parsed into typed values, never
    concatenated into a query ([[../f9-search-and-api/sad|F9 SAD]] §8).
  - A new command shipped without a capability → the registry convention test fails the build (AC-12).
  - Command flooding → per-chat rate limit with a single throttle message, not a message per command.
- **Security review:** N/A — no new external surface; long polling is outbound, and every control is
  inherited from F5 and F9.

## 7. Metrics / KPIs

- **Commands per week, by command** — shows which parts of the system the Owner actually reaches for.
  A command at zero for a month is a candidate for removal.
- **Unknown-command rate** — target < 5%. Higher means the catalogue or the naming is wrong.
- **`/hidden` and `/prefs` usage after a suppression spike** — the signal that visibility is working.
- **Recovery commands used vs runbook steps executed** — how much of [[../../operations/runbooks|R1–R10]]
  the chat actually replaces.

## 8. Open questions

- [ ] Should `/run` be available at all, or is triggering a Run too easy to fumble? — owner: Viacheslav
  — *default: available, behind a confirmation naming the estimated cost.*
- [ ] Does `/cost` show the running month or a rolling 30 days? — owner: Viacheslav — *default:
  calendar month, plus the current Run.*
- [ ] Should `/more` paginate the same digest or re-rank against fresh preferences? — owner: Viacheslav
  — *default: the same stored digest; a re-rank would make the ordering unstable mid-morning.*

## DoD self-check

- [x] Coverage types: happy (01, 02, 05, 06), error (08, 09, 11), authorization (07, 10), domain invariant (04, 12), cross-context (03)
- [x] No implementation tokens in §5 — no Telegram API names, no command literals, no JSON
- [x] Every US has ≥1 AC; NFRs measurable; open questions owned with defaults
