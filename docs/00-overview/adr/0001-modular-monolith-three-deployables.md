---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0001 — Modular monolith, three deployables, nine logical stages

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

[[../idea-brief|The brief]] specifies an event-driven pipeline of nine stages
(Discovery → Normalization → Deduplication → Enrichment → Matching → Ranking → Research →
Reporting → Delivery), and says each stage "should be an independent worker". The literal reading
is nine deployables. The system serves exactly one user, runs on a single-node k3s cluster, and is
built part-time by one engineer. We must decide the process topology without either (a) building a
distributed monolith of nine pods that each idle 23 hours a day, or (b) collapsing the stages into
a procedural script that demonstrates nothing and cannot be resumed.

## Decision drivers

- The Batch API lifecycle spans hours; stage boundaries must be durable, not stack frames.
- One user, one node: nine pods cost operational attention and buy no throughput.
- The repository is a portfolio artifact — message boundaries must be real, not simulated in-process.
- Solo, part-time delivery: process count is directly a schedule cost (SAD §2 C9).
- A future split must not be a rewrite.

## Considered options

1. **Nine microservices, one per stage.**
2. **Single process, in-memory pipeline, stages as method calls.**
3. **Modular monolith: one solution, three deployables (`Api`, `Worker`, `Telegram`), nine stages as separate Wolverine message handlers over RabbitMQ queues.**

## Decision outcome

**Chosen: Option 3.**

Every stage is an independent message handler with its own queue, its own retry policy, its own
idempotency key and its own metrics — the boundaries are genuinely asynchronous and genuinely
durable. All nine handlers are *hosted* by one `JobHunter.Worker` process, because at one user
there is nothing to gain from nine schedulers, nine health probes and nine image builds.

`JobHunter.Api` is separate because it has a different scaling and exposure profile (HTTP,
internet-facing, stateless). `JobHunter.Telegram` is separate because long-polling must be a
single consumer and its failure must not take the pipeline down.

Splitting a stage out later is a manifest change plus a `csproj` reference: create a new host that
calls `AddJobHunterApplication()` and subscribes to that one queue. No handler code changes,
because handlers already communicate only through `JobHunter.Contracts`.

## Consequences

**Positive**
- Real message boundaries, real backpressure, real replay-on-failure — the interesting properties are present and testable.
- Three images, three deployments, one migration path. Operable by one person.
- Stage-level observability from day one: one span and one metric label per stage.

**Negative**
- `jobhunter-worker` must run as exactly one replica while Hangfire schedules and the Run
  orchestrator are singleton-by-design. No HA on the pipeline (SAD §11 D2).
- A poison message in one stage occupies a shared process; mitigated by per-handler error policies
  and a dead-letter queue per stage.

**Neutral**
- The nine-stage vocabulary from the original plan is preserved verbatim in queue names, so the
  documentation and the runtime agree.

## Links

- Brief: [[../idea-brief]] §7 Approach C, §8 Engineer
- SAD: [[../sad]] §4 S1, §5, §7, §11 D2
- Related: [[0002-rabbitmq-wolverine-transport]], [[0004-hangfire-scheduling]]
