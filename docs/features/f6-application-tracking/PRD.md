---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f6-application-tracking, mvp, jobhunter]
---

# PRD — f6-application-tracking

> **Inputs:** [[../../CONTEXT]] §1 (Application, ApplicationStatus) · [[../f5-daily-digest-telegram/PRD|F5 PRD]] AC-03

## 1. Context

By M4 the Owner can triage a morning digest in ninety seconds. What they cannot do is answer "where
am I with Stripe?" — the taps produced a state, but nothing recorded how it got there, when, or what
happened next.

Job hunting at senior level runs twenty to forty concurrent conversations over several months. The
questions that matter are boring and constant: what have I applied to, what has gone quiet, what needs
a follow-up this week, what did I actually say to them. A spreadsheet answers these, which is why
everyone keeps one and why everyone's is out of date by week three — the cost of updating it is
exactly the cost of not updating it.

F6's premise is that the pipeline should update itself from actions the Owner is already taking, and
ask for the rest at the moment it is cheapest to give.

There is a second, quieter reason to build it. An application that reaches `Interview` or `Offer` is
the strongest preference evidence the system will ever collect — vastly more informative than a tap on
a card, because it survived a real filter. F7 is substantially better with that data than without it.

## 2. Goals

- Track every opportunity the Owner engages with through its full lifecycle.
- Record how each application reached its current state, and when.
- Let the Owner update a status in one tap or one command, from the digest or from anywhere.
- Surface what needs attention: gone quiet, closing soon, awaiting a reply.
- Feed outcomes back as high-quality preference evidence.

## 3. Non-goals

- Applying on the Owner's behalf ([[../../CONTEXT]] invariant 7). `Applied` is a status the Owner
  sets, never an action the system performs.
- Email or calendar integration. Recorded in [[../../BACKLOG]].
- Interview preparation material — a post-MVP candidate.
- Recruiter or contact management. A job has a company; it does not have a CRM.
- Learning from the outcomes — F7 fits the model; F6 supplies the evidence.

## 4. User stories

### US-01: Know where everything stands
**As the** Owner **I want** every opportunity I have engaged with grouped by where it is
**so that** I can see my whole pipeline in one view.

### US-02: Update status without friction
**As the** Owner **I want** to move something forward in one tap or one short command
**so that** keeping the pipeline current costs nothing.

### US-03: See how something got here
**As the** Owner **I want** the history of each application **so that** I can remember when I applied
and what has happened since.

### US-04: Be reminded of what has gone quiet
**As the** Owner **I want** to be told when something has sat untouched too long
**so that** a follow-up is not forgotten.

### US-05: Keep notes where the application is
**As the** Owner **I want** to attach a note to an application **so that** what I learned is not in a
different app.

### US-06: Not lose an application when the posting closes
**As the** Owner **I want** an application to survive its job posting closing **so that** my history
is intact.

### US-07: Have outcomes improve future recommendations
**As the** Owner **I want** what actually happened to influence what I am shown
**so that** the system learns from results, not only from taps.

## 5. Acceptance criteria

### AC-01 (US-01) — happy path
**Given** opportunities the Owner has acted on
**When** the pipeline is requested
**Then** each is presented under its current stage, with the most recently active first.

### AC-02 (US-02) — domain invariant
**Given** an application in any stage
**When** a stage change is requested
**Then** the change is applied if the transition is permitted, refused with the reason if not, and in
neither case is the recorded history altered.

### AC-03 (US-03) — domain invariant
**Given** any application
**When** its history is inspected
**Then** every stage it has occupied is listed with when it entered, and how the change was initiated.

### AC-04 (US-02) — cross-context
**Given** the Owner acts on a card in the morning digest
**When** the action is recorded
**Then** the corresponding application is created or advanced, and the action appears in its history.

### AC-05 (US-04) — happy path
**Given** an application that has not changed for longer than its stage's threshold
**When** the reminder sweep runs
**Then** the Owner is told once, with what to do about it, and is not told again until it changes or
the threshold passes again.

### AC-06 (US-05) — happy path
**Given** an application
**When** the Owner attaches a note
**Then** it is stored against the application with its time, and appears in the history view.

### AC-07 (US-06) — cross-context
**Given** an application whose job posting has closed
**When** the closure is processed
**Then** the application is retained with its full history and is marked as having a closed posting,
rather than being removed.

### AC-08 (US-07) — cross-context
**Given** an application reaches a terminal outcome
**When** it is recorded
**Then** evidence of that outcome, with the opportunity's characteristics, is retained for preference
learning and is weighted more strongly than a card action.

### AC-09 (US-01) — authorization
**Given** a request to read or change an application
**When** it arrives from anyone other than the Owner
**Then** it is refused and nothing is changed.

### AC-10 (US-03) — error path
**Given** a stage change that would produce an impossible sequence
**When** it is attempted
**Then** it is refused with an explanation, and the application is unchanged.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Pipeline view latency | < 500 ms for 200 applications | Query benchmark |
| Status update round trip | < 1 s from tap to confirmation | Callback latency |
| Reminder precision | 0 duplicate reminders for one condition | Integration test |
| History completeness | 100% of stage changes recorded | Assertion on every transition |
| Transition legality | 100% of illegal transitions refused | Transition matrix test |

## 6.1 Security / privacy

- **Data classification:** confidential — the pipeline reveals where the Owner is interviewing.
- **Personal data touched:** none beyond what the Owner types into notes.
- **AuthZ/AuthN impact:** all reads and writes are owner-scoped (AC-09), through both the bot
  allowlist and the API scope.
- **Abuse cases:**
  - Pipeline exposure through an unauthenticated endpoint → owner scope required; the fallback-deny
    policy from F0 means a new endpoint is protected by default.
  - Note content in logs → notes are never logged, only their length.
  - A forged status change from a callback → the same signed short id and allowlist as F5.
- **Security review:** N/A — no new external surface, no new personal-data category.

## 7. Metrics / KPIs

- **Pipeline freshness** — proportion of applications whose stage changed within 14 days. A falling
  number means the tracking has become a chore, which is the failure mode to watch for.
- **Applications per week** — informational; the Owner's own throughput.
- **Interview conversion** — applications reaching `Interview` ÷ applications reaching `Applied`. The
  best available proxy for whether the ranking is actually working.
- **Reminder action rate** — proportion of reminders followed by a status change within 3 days. Low
  means the reminders are noise and their thresholds are wrong.

## 8. Open questions

- [ ] Reminder thresholds per stage — owner: Viacheslav — *default: `Applied` 10 days, `Interview`
  7 days, `Saved` 5 days then a nudge to apply or drop.*
- [ ] Should `Saved` items be auto-dropped if their posting closes? — owner: Viacheslav —
  *default: no; mark and keep, the history has value.*
- [ ] Should a closing posting trigger a re-surface reminder for saved items? — owner: Viacheslav —
  *default: yes, one reminder.*

## DoD self-check

- [x] Coverage types: happy (01, 05, 06), error (10), authorization (09), domain invariant (02, 03), cross-context (04, 07, 08)
- [x] No implementation tokens in §5
- [x] Every US has ≥1 AC; NFRs measurable
