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

# F5-0002 — A per-card delivery log as the idempotence mechanism

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Delivery is a loop of twelve network calls to a third party. Any of them can fail; the process can be
restarted mid-loop by a deploy or a crash; the triggering message can be redelivered by the broker
(at-least-once, by design — [[../../../00-overview/adr/0007-transactional-outbox|ADR-0007]]).

[[../../../CONTEXT]] invariant 8 says a card is delivered at most once. We must decide the mechanism,
and the decision is mostly about *when* the record is written relative to the send.

Getting this wrong is uniquely damaging: duplicate cards in a morning digest look like a broken system
in the one artifact the Owner judges the whole product by.

## Decision drivers

- Message redelivery is normal and must be safe, not merely unlikely.
- A restart mid-delivery must resume exactly, not restart from the beginning.
- The `/digest` command re-renders the same digest and must not interact with delivery state at all.
- The mechanism must be inspectable — "was this card sent, and when" should be a query.
- Telegram provides no idempotency key of its own, so the mechanism must be entirely ours.

## Considered options

1. **A delivered flag on the digest** — set after the whole batch.
2. **A flag per card**, updated after each send.
3. **A separate `delivery_log` table with a unique constraint**, one row written immediately after
   each successful send.
4. **Rely on Wolverine's inbox** for message-level deduplication.

## Decision outcome

**Chosen: Option 3.**

`delivery_log(run_id, chat_id, card_key)` with a unique index, and a row inserted **immediately after
each successful send**, not after the batch. Delivery begins by loading the already-delivered card
keys for `(run_id, chat_id)` and sending only the remainder.

Three details make it work:

1. **`card_key` is deterministic** — `sha256(run_id ‖ job_id)` truncated. A resumed delivery
   recomputes the same keys, so it can ask "which of these have I already sent" without any
   coordination.
2. **The header and footer use reserved keys** (`__header__`, `__footer__`), so they go through the
   same mechanism instead of needing a special case that will eventually be got wrong.
3. **The table is append-only.** No update path, no delete path. Deleting a row would mean
   re-delivering, which is exactly the failure the table exists to prevent.

There remains a one-statement window between a successful send and the log insert. A crash there
re-sends that single card. This is accepted deliberately: an at-least-once send with a one-card
duplicate is strictly better than an at-most-once send that can drop a card, and dropping is the
failure the Owner cannot detect.

Option 1 loses everything on a mid-batch crash. Option 2 works but couples delivery state to the card
row, so replaying a digest for any other reason risks mutating it. Option 4 deduplicates the
*message*, not the *sends* — it would not help a crash halfway through the loop, which is the case
that actually happens.

## Consequences

**Positive**
- Interruption at any point resumes exactly. Verified by killing delivery at several points and
  asserting the final message count.
- "What was sent, when, with which Telegram message id" is a query, which makes
  [[../../../operations/runbooks|R1]]'s re-delivery step safe to run.
- `/digest` re-renders without touching the log, so it can never re-deliver.
- Two racing delivery handlers cannot double-send — the constraint arbitrates.

**Negative**
- One extra insert per message. Irrelevant at twelve messages a day.
- The log grows unboundedly. Pruned with the digest at 180 days; a delivered-card record older than
  that has no operational value.
- The one-statement crash window can duplicate a single card. Accepted, and documented, as the safe
  direction of the trade.

**Neutral**
- The same table serves any future channel by varying `chat_id`, without schema change.

## Links

- [[../../../CONTEXT]] invariant 8 · [[../PRD]] AC-04 · [[../sad]] §10 QG-2
- [[../data-model]] §delivery_log · [[../test-plan]] §The duplicate-delivery suite
