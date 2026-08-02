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

# 0002 — RabbitMQ as transport, Wolverine as the handler framework

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Nine stages must exchange messages durably ([[0001-modular-monolith-three-deployables|ADR-0001]]).
The helios cluster already runs a shared RabbitMQ instance with per-project vhosts, and the sibling
`overflow` project already uses Wolverine over it in production. We must pick the transport and the
library that maps messages to handlers, and the choice must not force us to hand-roll an outbox.

## Decision drivers

- RabbitMQ is already provisioned, monitored and backed up on helios — a new broker is a new
  operational surface for zero benefit.
- The pipeline needs a *transactional outbox*: a state change and its event must commit atomically,
  or QG-2 (resumability, no duplicate spend) is unachievable.
- Handler discovery and per-message retry/error policy should be declarative, not bespoke.
- Proven in a sibling project reduces schedule risk.

## Considered options

1. **Kafka + a hand-written consumer host.**
2. **RabbitMQ + MassTransit.**
3. **RabbitMQ + Wolverine.**
4. **PostgreSQL-only queueing (`FOR UPDATE SKIP LOCKED`), no broker.**

## Decision outcome

**Chosen: Option 3.** RabbitMQ (helios shared instance, vhost `jobhunter-{env}`) with Wolverine as
the messaging framework.

Wolverine gives handler discovery by convention, per-handler retry/requeue/dead-letter policies, and
— decisively — a first-class **EF Core transactional outbox and inbox** so `SaveChangesAsync` and
`PublishAsync` share one transaction ([[0007-transactional-outbox|ADR-0007]]). Queue naming follows
the `overflow` convention: `{MessageType.FullName}.{ApplicationName}`, with `AutoProvision` on.

Kafka is rejected: no replay or log-compaction requirement exists at ~300 messages/day, and it would
add a cluster component nobody else on helios needs. MassTransit is a reasonable alternative but its
outbox story is heavier and the team (one person) already has Wolverine muscle memory.
Postgres-only queueing is genuinely viable at this volume, but it would make the "event-driven
architecture" claim a matter of interpretation rather than fact, and the broker is free.

## Consequences

**Positive**
- Atomic state-change-plus-publish, which is the precondition for exactly-once-effective processing.
- Declarative retry and dead-lettering per stage; failures are visible in the RabbitMQ management UI.
- Zero new infrastructure; vhost isolation keeps JobHunter off other projects' queues.

**Negative**
- A framework dependency in the `Application` layer's handler signatures. Contained by keeping
  handlers thin and pushing logic into plain services that are unit-testable without Wolverine.
- RabbitMQ is not a log: a message consumed and acked is gone. Recovery comes from Postgres plus the
  outbox, not from the broker.

**Neutral**
- Local development uses the Aspire-provisioned RabbitMQ container
  ([[0013-aspire-local-dev-only|ADR-0013]]), the same client code path.

## Links

- SAD: [[../sad]] §4 S2, §6, §8
- Related: [[0001-modular-monolith-three-deployables]], [[0007-transactional-outbox]], [[0003-postgresql-efcore-dapper]]
