---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f6-application-tracking, jobhunter]
---

# F6-0001 — Permissive transitions with complete, immutable history

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

[[../../../CONTEXT]] names six statuses and implies an order: `New → Saved → Applied → Interview →
Rejected | Offer`. The obvious implementation is a strict state machine that refuses anything off the
happy path.

Real hiring does not respect that diagram. A recruiter approaches directly and the first event is an
interview. A company rejects, then re-opens the role three weeks later. The Owner taps `Applied` on
the wrong card at 07:03 and needs to undo it. An offer is declined. Each of these is ordinary, and a
tool that refuses to record them stops being used within a fortnight — the classic failure mode of
process software.

The opposite extreme — allow anything — makes the history meaningless and hides genuine mistakes.

## Decision drivers

- The pipeline is only useful if it is current, and it is only current if updating it never fights
  the Owner ([[../PRD]] §7 — pipeline freshness is the metric to watch).
- Terminal outcomes are the system's best preference evidence
  ([[../../f7-preference-learning/index|F7]]); a refused transition is evidence lost.
- History must be trustworthy: a correction should be visible as a correction, not as a rewrite.
- Genuinely impossible sequences should still be caught, because they indicate a bug rather than a
  real-world oddity.

## Considered options

1. **Strict state machine** — only forward transitions along the canonical path.
2. **No rules** — any status to any status.
3. **Permissive transitions with a small refused set, and a complete append-only history.**
4. **Strict rules plus an override flag** for the exceptional cases.

## Decision outcome

**Chosen: Option 3.**

A transition table enumerates what is permitted. It refuses only sequences that cannot correspond to
anything real:

| Refused | Why |
|---|---|
| `Offer → Ignored` | An offer is accepted or declined; declining is `Rejected` |
| `Rejected → Interview` | If the conversation genuinely re-opened, that is a new application; the message says so |
| `Interview → Applied` | Going backwards through the funnel is not a real event; it is a mis-tap, and `Applied → Interview` already exists to fix it |
| `Offer → Interview` / `Offer → Applied` | Same reasoning |

Everything else is permitted, including corrections (`Applied → Saved`), re-affirmations
(`Applied → Applied`), multiple interview rounds (`Interview → Interview`), inbound approaches
(`New → Interview`), and re-opened roles (`Rejected → Applied`).

Two things make this safe rather than sloppy:

1. **`application_transitions` is append-only.** Every change is recorded with its time and its
   source (`Telegram`, `Api`, `System`). A correction appears as a correction. There is no update
   path and no delete path on the table.
2. **Every refusal names a remedy.** `Rejected → Interview` returns a message telling the Owner to
   create a new application if the company re-opened the conversation. A refusal without a remedy is
   just an obstacle.

Option 4 was tempting, but an override flag always becomes the default path — it converts a design
decision into a habit of dismissing a warning.

## Consequences

**Positive**
- The pipeline stays current because updating it never argues with reality.
- Terminal outcomes are always recordable, so F7 gets its strongest evidence.
- Corrections are visible as corrections, which is more informative than a silently rewritten status.
- The `source` column separates a system-driven change from a deliberate one, which matters when
  reading a history months later.

**Negative**
- A history can contain a confusing sequence if the Owner taps carelessly. Acceptable — it is visible
  and explicable, whereas a refused legitimate transition is invisible data loss.
- Analytics must handle non-linear paths. The conversion metric counts *ever reached `Interview`*
  rather than assuming a single forward walk.

**Neutral**
- The table is enumerated by the test suite over the full status product, so adding a status
  automatically expands the coverage and fails until the rules are updated.

## Links

- [[../PRD]] AC-02, AC-10 · [[../sad]] §4 S1, §10 QG-1
- [[../contracts/application-api]] §Transition matrix · [[../test-plan]] §The transition matrix suite
