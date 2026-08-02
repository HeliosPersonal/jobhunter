---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f3-claude-batch-enrichment, jobhunter]
---

# F3-0001 — The Run as a durable, resumable aggregate

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

A day's intelligence work spans roughly five hours, most of it spent waiting for an asynchronous
provider. During that window the worker will be restarted — by a deploy, by a node reboot, by an
OOM kill, by a `kubectl rollout restart` at an unfortunate moment. This is not an exceptional case
to be handled defensively; at daily cadence over months it is a certainty.

We must decide where the progress of that work lives. The answer determines whether an interruption
costs nothing, costs a duplicate charge, or costs the day.

## Decision drivers

- Anthropic charges per submission. A resubmission after a crash is real money for work already paid for.
- The provider's batch id is the only handle on in-flight work; losing it means the work is
  unreachable and must be redone.
- The Owner notices a missing digest; they do not notice a silently duplicated charge. The system
  must protect against the failure the Owner cannot see.
- F4, F5 and F8 have the same shape and should not each reinvent this.
- QG-2 in the system SAD makes resumability a stated quality goal, not an implementation detail.

## Considered options

1. **In-memory orchestration** — a long-running method with `await`s across the polling loop.
2. **Hangfire continuations** — chain jobs, let Hangfire persist the chain state.
3. **The Run as a database aggregate with an explicit state column**, plus stateless handlers that
   read state, act, and advance it.
4. **A workflow engine** (Temporal, Elsa, MassTransit sagas).

## Decision outcome

**Chosen: Option 3.**

`runs.state` is an explicit column over a closed set of values. Each handler is stateless: it loads
the Run, does one step, and commits the new state together with its outbox message
([[../../../00-overview/adr/0007-transactional-outbox|ADR-0007]]). On startup the orchestrator queries
for non-terminal Runs and re-enters each at whatever state it is in.

Three rules make this actually work rather than merely look tidy:

1. **`provider_batch_id` is persisted immediately on submit**, before anything else happens. It is
   the single fact that makes in-flight work recoverable.
2. **Unique `(run_id, stage, tier)` on `batches`.** A resumed Run that mistakenly tried to resubmit
   would violate the constraint and fail loudly rather than pay twice. The database enforces the
   invariant, not the code path.
3. **Every write is an upsert on a natural key** — enrichments on `(job_id, run_id)`, items on
   `(batch_id, custom_id)` — so replaying a partially-processed result set converges rather than duplicating.

Option 1 loses everything on restart. Option 2 puts the state inside Hangfire's tables, where it is
neither queryable in domain terms nor visible in the digest, and where a Hangfire upgrade becomes a
data migration of our business state. Option 4 is the right answer at a scale this project does not
have; it would add an operational component to a single-node cluster to solve a problem that one
column and two unique indexes already solve.

## Consequences

**Positive**
- An interruption at any point costs nothing but a restart. Verified by an eight-case crash matrix.
- Run state is ordinary SQL: "what happened last night" is a query, not a log trawl.
- F4, F5 and F8 reuse the aggregate unchanged — they add a `stage` value, not a mechanism.
- The state machine is testable without any of the infrastructure it coordinates.

**Negative**
- Every step is a database round trip. Irrelevant at nine state transitions a day.
- The state machine must be kept honest as stages are added; an illegal transition is a defect
  rather than a compile error. Mitigated by an exhaustive transition test.
- A crash in the one-statement window between `SubmitAsync` returning and the batch row committing
  could still orphan a submission. Mitigated by reconciling against the provider's recent-batch list
  on startup before submitting anything (SAD §11 D5).

**Neutral**
- `CostAborted` is a terminal state that still flows into reporting, because a reduced digest is
  better than silence.

## Links

- [[../sad]] §6.1, §10 QG-1 · [[../data-model]] §runs · [[../test-plan]] §The crash matrix
- [[../../../00-overview/adr/0007-transactional-outbox|ADR-0007]]
