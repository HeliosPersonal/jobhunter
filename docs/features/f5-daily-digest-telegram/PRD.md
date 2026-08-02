---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f5-daily-digest-telegram, mvp, jobhunter]
---

# PRD — f5-daily-digest-telegram

> **Inputs:** [[../../CONTEXT]] §1 (Digest, Card, Signal) · [[../../00-overview/idea-brief|idea-brief]] §8 (UX-researcher) · [[../../00-overview/sad|SAD]] §6.3
> **External context:** [[../../DECISION-LOG|D2, D7]], [[../../ARCHITECTURE-OPEN-DECISIONS|O8]]

## 1. Context

The interaction surface is a Telegram message read half-awake with one thumb. That is a severe
constraint and it should drive the design rather than be accommodated by it
([[../../00-overview/idea-brief|brief]] §8).

Three things follow. First, the opening message must answer *is today worth my attention* in under
three seconds — counts, best match, salary statistic, and nothing else above the fold. Second, cards
must be scannable, not read: title, company, score, three reasons, four buttons. Third, a tap must be
acknowledged instantly, because a button that appears to do nothing destroys trust in the entire
system in one interaction.

There is a fourth thing, and it is the one most easily got wrong: **ignoring must feel productive**.
If the Owner learns that ignoring teaches the system, the ignore rate becomes engagement rather than
churn. The digest saying "I stopped showing you 34 jobs below your salary floor" is the single
strongest retention mechanism available ([[../../DECISION-LOG|D7]]), and it is why suppression is
reported rather than silent.

The 07:00 slot is a commitment. A digest that arrives at 09:30 has missed the moment it was designed
for; a partial digest at 07:00 is worth more than a complete one later
([[adr/0001-never-delay-the-digest|ADR-F5-0001]]).

## 2. Goals

- Deliver one message sequence every morning at 07:00 Europe/Kyiv, without exception.
- Let the Owner decide in three seconds whether today merits attention.
- Present each opportunity so it can be judged in one glance, with the reasons behind its ranking.
- Make every action one tap, instantly acknowledged, and never lost.
- Tell the Owner what was hidden and why.
- Capture every action as evidence for preference learning, from the very first digest.

## 3. Non-goals

- Deciding *what* ranks where — F4 owns scoring.
- Managing the application pipeline beyond recording the initial action — F6 owns statuses.
- Learning from the actions — F7 fits the model; F5 only captures the evidence.
- Any channel other than Telegram ([[../../DECISION-LOG|D2]]).
- Conversational interaction. The bot answers a small fixed command set, it is not a chat interface.

## 4. User stories

### US-01: Know in three seconds whether today matters
**As the** Owner **I want** the opening message to state how many opportunities there are and how
good the best one is **so that** I can close the app or keep reading without thinking.

### US-02: Judge an opportunity at a glance
**As the** Owner **I want** each card to show the role, the company, its score and why
**so that** I can decide without opening anything.

### US-03: Act in one tap
**As the** Owner **I want** to open, ignore, save or mark as applied without leaving the message
**so that** triage costs seconds.

### US-04: Trust that my tap registered
**As the** Owner **I want** immediate visible acknowledgement of every action **so that** I never
wonder whether it worked.

### US-05: Know what was hidden
**As the** Owner **I want** to be told how many opportunities were suppressed and why
**so that** I can tell a working filter from a broken one.

### US-06: Get the digest even on a bad day
**As the** Owner **I want** a digest every morning regardless of what failed **so that** silence
always means something is wrong.

### US-07: Never see the same card twice
**As the** Owner **I want** each opportunity delivered once **so that** a retry or a restart does not
spam me.

### US-08: Ask for things between digests
**As the** Owner **I want** a few commands — today's digest again, my saved list, my pipeline, a
search **so that** I am not limited to the 07:00 moment.

## 5. Acceptance criteria

### AC-01 (US-01) — happy path
**Given** a completed day of ranking
**When** the morning delivery runs
**Then** the first message states the number of new opportunities, how many are strong matches, a
salary statistic and the single best opportunity, and nothing else.

### AC-02 (US-02) — domain invariant
**Given** any opportunity presented to the Owner
**When** it is rendered
**Then** it carries at least one reason for its ranking; an opportunity with no reasons is not
presented.

### AC-03 (US-03, US-04) — happy path
**Given** a presented opportunity
**When** the Owner takes one of the four actions
**Then** the action is recorded, the Owner sees confirmation within the message, and the card
reflects its new state.

### AC-04 (US-07) — domain invariant
**Given** an opportunity already delivered for a given day
**When** delivery is retried or the process restarts mid-delivery
**Then** it is not delivered again.

### AC-05 (US-06) — error path
**Given** a day in which no new opportunities were found
**When** the morning delivery runs
**Then** a digest is still delivered, stating plainly that there was nothing new.

### AC-06 (US-06) — error path
**Given** a day in which part of the analysis did not complete
**When** the delivery time arrives
**Then** the digest is delivered on time with what completed, and states what is missing and why.

### AC-07 (US-05) — cross-context
**Given** opportunities suppressed by preference or threshold
**When** the digest is delivered
**Then** it states how many were suppressed and the main reasons.

### AC-08 (US-03) — cross-context
**Given** an action taken on an opportunity
**When** it is recorded
**Then** evidence of that action, with the opportunity's characteristics at that moment, is retained
for preference learning.

### AC-09 (US-04) — error path
**Given** an action on an opportunity that no longer exists or has closed
**When** the Owner taps
**Then** they are told plainly, and no invalid state is recorded.

### AC-10 (US-08) — authorization
**Given** an interaction arriving from anyone other than the Owner
**When** it is received
**Then** it is discarded before any processing, and the attempt is recorded.

### AC-11 (US-02) — domain invariant
**Given** an opportunity whose application destination is no longer reachable
**When** the digest is assembled
**Then** it is not presented as an actionable card.

### AC-12 (US-08) — happy path
**Given** the Owner issues one of the supported commands
**When** it is processed
**Then** the requested information is returned in the same scannable form as the digest.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Delivery time | 07:00 ±3 min Europe/Kyiv, every day | `jobhunter.digest.delivered_at` |
| Time to first useful signal | header readable in < 3 s | Layout review; header ≤ 6 lines |
| Card count | score ≥ 70, capped at 10 | Configuration, asserted |
| Action acknowledgement | < 1 s from tap to visible confirmation | Callback latency metric |
| Duplicate deliveries | **0**, ever | `delivery_log` uniqueness |
| Delivery success | ≥ 99.5% of cards delivered on first attempt | Metric |
| Apply-link liveness | 100% of presented cards verified reachable | Pre-delivery check |
| Message rendering | 0 formatting failures across the corpus | Rendering corpus test |

## 6.1 Security / privacy

- **Data classification:** the digest contains public job data plus internal scores. **No CV content,
  ever** — the boundary drawn in F4 holds here.
- **Personal data touched:** none beyond the Owner's chat id.
- **AuthZ/AuthN impact:** the chat-id allowlist is the only authorisation, applied before routing
  (AC-10) ([[../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]]).
- **Abuse cases:**
  - An unknown chat interacting with the bot → dropped before any handler runs; the attempt is logged
    at warning level with the chat id (AC-10).
  - A forged action payload → payloads carry a signed short identifier; the referenced opportunity is
    validated to exist and to belong to a delivered digest.
  - Hostile content in a job title or company name reaching the message → all dynamic text is escaped
    for the message format; the rendering corpus includes deliberate injection attempts.
  - Bot token exposure → Infisical at runtime; never logged, never in an image layer.
- **Security review:** N/A — no personal data, one authorised chat, no inbound HTTP surface (long
  polling is outbound).

## 7. Metrics / KPIs

- **On-time delivery rate** — target 100%. Any miss is an incident ([[../../operations/runbooks|R1]]).
- **Action rate** — proportion of delivered cards receiving any action. Target ≥ 60%; below that the
  cards are not scannable enough.
- **Open rate among top 3** — the practical proxy for `precision@10`.
- **Ignore rate** — reported, not targeted. A high ignore rate with rising precision is healthy; it
  is how F7 learns.
- **Duplicate deliveries** — target zero, permanently.

## 8. Open questions

- [ ] Card count: fixed top-10 or score-threshold driven? — owner: Viacheslav — *default: score ≥ 70
  capped at 10, and report the count above the threshold.* ([[../../ARCHITECTURE-OPEN-DECISIONS|O8]])
- [ ] Should the digest include a weekly summary on Mondays? — owner: Viacheslav — *default: no for
  M4; a candidate for M5.*
- [ ] Should saved opportunities be re-surfaced if they are about to close? — owner: Viacheslav —
  *default: yes, one reminder, owned by F6.*

## DoD self-check

- [x] Coverage types: happy (01, 03, 12), error (05, 06, 09), authorization (10), domain invariant (02, 04, 11), cross-context (07, 08)
- [x] No implementation tokens in §5 — no Telegram API names, no JSON, no SQL
- [x] Every US has ≥1 AC; NFRs measurable; open questions owned with defaults
