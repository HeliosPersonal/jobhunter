# T16 — replay-dlq CLI

**Layer:** ops · **Deps:** T08 · **Est:** M · **Owner:** Viacheslav

## What

The `replay-dlq` CLI command hosted in `JobHunter.Worker/Cli/`, referenced by
[[../sad|SAD]] §5 and runbook R6. It lists dead-lettered messages per stage, and moves a selected
message (or a whole dead-letter queue) back onto its source queue for reprocessing, so a poisoned or
transiently failed message can be recovered without a terminal into RabbitMQ.

## Done when

- `dotnet run --project src/JobHunter.Worker -- replay-dlq --list` shows dead-lettered messages
  grouped by stage with their failure reason.
- `replay-dlq --queue <name>` re-enqueues the queue's dead-lettered messages onto the source queue.
- Replay is idempotent against the consumer's inbox — a replayed message that was already processed is
  a no-op, not a duplicate side effect (invariant 8 / gate G4).
- The command refuses to run against an empty or unknown queue with a clear message, not a stack trace.
- An integration test dead-letters a message, replays it, and asserts single-effect reprocessing.

## Out of scope

- A UI for replay (the CLI is the interface).
- Automatic replay policies (replay is an operator decision).

## Links

[[../sad]] §5 · [[../../../operations/runbooks]] R6 · [[../../../00-overview/adr/0002-rabbitmq-wolverine-transport|ADR-0002]]
