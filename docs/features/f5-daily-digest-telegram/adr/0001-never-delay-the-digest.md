---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f5-daily-digest-telegram, jobhunter]
---

# F5-0001 — 07:00 is a hard commitment; ship partial rather than late

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The daily Run starts at 02:00 and depends on an asynchronous provider whose stated turnaround can be
up to 24 hours. Most mornings everything completes by 05:00. Some mornings it will not. We must decide
what happens at 07:00 when the analysis is incomplete.

The temptation is to wait — a complete digest is obviously better than an incomplete one. But the
07:00 slot is not an arbitrary timestamp; it is a habit. The product's entire proposition is *one
read, first thing, then get on with your day*
([[../../../00-overview/idea-brief|brief]] §8, [[../../../DECISION-LOG|D2]]). A digest arriving at
09:30 has missed the moment it was designed for and will be read, if at all, as an interruption.

## Decision drivers

- The value of the digest is time-dependent in a way its completeness is not.
- A habit forms on reliability, not on quality. An unreliable 07:00 message stops being checked.
- **Silence is the worst outcome.** A missing digest is indistinguishable from a broken system, and
  the Owner will spend time investigating rather than reading.
- Carried-over jobs are not lost — they appear tomorrow, one day later, in a product whose latency
  budget is already a day.
- [[../../../CONTEXT]] invariant 6 already requires a cost abort to still produce a digest; the same
  logic applies to every other partial-failure mode.

## Considered options

1. **Wait for completion**, deliver whenever ready.
2. **Wait up to a grace period** (say 30 minutes), then deliver partial.
3. **Deliver at 07:00 unconditionally**, with whatever completed, stating plainly what is missing.
4. **Deliver at 07:00, then send a supplementary message** when the remainder completes.

## Decision outcome

**Chosen: Option 3.**

At 06:45 the assembler runs against whatever state the Run is in. Every path produces a digest:

| Run state at 06:45 | Digest |
|---|---|
| `Reporting` / `Delivered` | Normal |
| `Enriching` / `Matching` | Partial, stating how many are still being analysed |
| `CostAborted` | Reduced, with a visible budget warning and what to do about it |
| No Run at all | Empty, stating that plainly and that nothing is wrong |

Incomplete work is **carried over**, not discarded: the batch keeps polling and its results land in
tomorrow's Run. The count is reported so the Owner knows the difference between "nothing found" and
"not finished".

Option 4 is rejected specifically because it reintroduces the interruption the product exists to
remove. A second message at 11:00 is exactly the notification pattern
[[../../../DECISION-LOG|D2]] rejected. Option 2's grace period only moves the problem — either the
grace expires and we are at Option 3 anyway, or it does not and we are at Option 1.

## Consequences

**Positive**
- The 07:00 message is unconditional, which is what makes it a habit rather than a notification.
- Silence always means something is broken, so an absent digest is an unambiguous alert
  ([[../../../operations/runbooks|R1]]).
- Every degraded mode has a defined, tested, human-readable presentation rather than an ad-hoc one.
- The Owner learns to trust the message rather than to check the system.

**Negative**
- Some mornings the digest is smaller than it could have been. Acceptable: those jobs appear tomorrow,
  and the product's latency budget is a day.
- Four extra rendering variants to design, test and maintain. They are in the rendering corpus, which
  makes them cheap to keep correct.

**Neutral**
- The 06:45 assembly time is a configuration value; moving delivery is a config change, and the
  15-minute gap absorbs assembly and link verification.

## Links

- [[../PRD]] AC-05, AC-06 · [[../sad]] §6.3, §10 QG-1
- [[../contracts/telegram-messages]] §Degraded-day variants
- [[../../../DECISION-LOG|D2]] · [[../../../CONTEXT]] invariant 6
