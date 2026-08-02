# T08 — Wolverine + RabbitMQ + transactional outbox

**Layer:** infra/messaging · **Deps:** T05 · **Est:** L · **Owner:** Viacheslav

## What

Wire Wolverine over RabbitMQ with conventional routing
(`{MessageType.FullName}.jobhunter-worker`), `AutoProvision`, per-handler retry policies, a
dead-letter queue per stage, and the EF Core transactional outbox and inbox
(`UseEntityFrameworkCoreTransactions()`, `PersistMessagesWithPostgresql()`). Handlers are discovered
by scanning `JobHunter.Application`, so a new handler needs no registration (SAD §10 QG-1).

## Done when

- A handler that throws after publishing leaves neither state change nor event observable (AC-03).
- A redelivered message produces exactly one effect and the duplicate is recorded (AC-04).
- A new handler class in `JobHunter.Application` is discovered with no wiring change.
- Each stage has its own dead-letter queue; a poison message lands there rather than blocking the queue.
- Messaging tests run against real Postgres **and** real RabbitMQ via Testcontainers.
- `wolverine.outgoing.backlog` is exported as a metric (feeds the alert in observability §4).

## Out of scope

- Any concrete pipeline event — those arrive with their features.

## Links

[[../../../00-overview/adr/0002-rabbitmq-wolverine-transport|ADR-0002]] · [[../../../00-overview/adr/0007-transactional-outbox|ADR-0007]] · [[../sad]] §6.2
