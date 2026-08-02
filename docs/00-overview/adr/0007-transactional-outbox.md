---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0007 — EF Core transactional outbox for event publication

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Every stage both writes to PostgreSQL and publishes an event to RabbitMQ. Doing these as two
independent operations produces the classic dual-write failure: either the Job is saved and the
`JobDiscovered` event is lost (the pipeline stalls silently), or the event is published and the
transaction rolls back (a downstream stage processes a Job that does not exist). At one Run per day,
a lost event is a lost day and it will not be noticed until 07:00.

## Decision drivers

- QG-2 requires that a crash at any point converges on re-run, with no duplicate LLM spend and no
  duplicate delivery.
- Anthropic spend is real money: reprocessing because an event was replayed is a direct cost.
- Wolverine already ships an EF Core outbox and inbox — this is a configuration decision, not a
  build decision ([[0002-rabbitmq-wolverine-transport|ADR-0002]]).

## Considered options

1. **Publish directly from the handler after `SaveChangesAsync`.**
2. **Publish before saving, compensate on failure.**
3. **Wolverine's EF Core transactional outbox: message persisted in the same transaction as the state change, relayed by a background sender.**
4. **Hand-rolled outbox table plus a polling relay.**

## Decision outcome

**Chosen: Option 3.**

`UseEntityFrameworkCoreTransactions()` plus `PersistMessagesWithPostgresql()`; every handler is
`[Transactional]`. Outgoing messages land in `wolverine_outgoing_envelopes` inside the same
transaction as the domain write, and a background sender relays them to RabbitMQ with retry. The
matching **inbox** (`wolverine_incoming_envelopes`) de-duplicates redelivery, so a message that is
processed but not acked before a crash is not processed twice.

On top of the framework guarantee, every stage carries a **domain-level idempotency key**, because
at-least-once delivery is still at-least-once:

| Stage | Idempotency key |
|---|---|
| Normalization / Deduplication | `RawPosting.content_hash` |
| Enrichment / Matching | unique `(run_id, job_id, stage)` |
| Delivery | unique `(run_id, chat_id, card_key)` (invariant 8) |

The rule: **a handler must be safe to run twice.** Every one of them is tested for exactly that.

## Consequences

**Positive**
- No dual-write window. A committed state change always produces its event, eventually.
- Redelivery is free of side effects, so aggressive retry is safe.
- Zero duplicate Anthropic charges on replay, because the unique constraint short-circuits before submission.

**Negative**
- Two extra tables in the application schema and a background relay to monitor. An outbox backlog
  metric and alert are required (`wolverine.outgoing.backlog`).
- Publication is now asynchronous: an event may lag its transaction by up to the relay interval.
  Irrelevant at daily cadence.

**Neutral**
- The outbox tables live in the same database as the domain, so a `pg_dump` restores a consistent
  system including in-flight messages.

## Links

- SAD: [[../sad]] §6.1, §8, §10 QG-2
- Related: [[0002-rabbitmq-wolverine-transport]], [[0003-postgresql-efcore-dapper]]
