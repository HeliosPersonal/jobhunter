---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "S"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0004 — Hangfire for scheduling, over Quartz.NET

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The plan offers "Quartz.NET (or Hangfire)". Four things need scheduling: Discovery every 6 hours,
the daily Run at 02:00, the digest at 07:00 Europe/Kyiv, and weekly preference recomputation plus
weekly ATS-binding re-detection. The Batch poller also needs delayed re-execution with backoff.
Schedules must survive process restarts and must be inspectable when something does not fire.

## Decision drivers

- Durable schedule state — a missed 02:00 because a pod restarted at 01:59 is a lost day.
- Delayed/continuation jobs with retry, for the poll-with-backoff loop.
- Operational visibility without building a UI.
- Precedent: `wisewizard` runs Hangfire on PostgreSQL under a `hangfire` schema, in production, on the same cluster.

## Considered options

1. **Quartz.NET with the ADO.NET job store.**
2. **Hangfire with `Hangfire.PostgreSql`.**
3. **Kubernetes `CronJob` objects.**
4. **A bare `PeriodicTimer` in a `BackgroundService`.**

## Decision outcome

**Chosen: Option 2.** Hangfire, storage `Hangfire.PostgreSql`, in the same database as the domain
under a dedicated `hangfire` schema.

Hangfire's dashboard (bound to cluster-internal only, behind the API's auth) answers "did it run,
did it fail, what was the exception, retry it now" without any code from us — which at one
operator is worth more than Quartz's richer trigger semantics, none of which we need. The
enqueue-with-delay primitive maps directly onto the Batch poll loop.

Kubernetes `CronJob` is rejected: it would need a separate image entrypoint per schedule and gives
no continuation, no retry history and no in-cluster visibility of *why* something failed. A bare
timer is rejected because schedule state would live only in memory.

## Consequences

**Positive**
- Durable, restart-safe schedules; retry history and a dashboard for free.
- Same database, same transaction infrastructure, same backup as domain data.
- Delayed jobs give the Batch poller its backoff without a custom scheduler.

**Negative**
- Hangfire's schema lives in our database and must be migrated on upgrades.
- The dashboard must never be exposed publicly — enforced by ingress rules and an authorization filter.
- Hangfire assumes a singleton server for recurring jobs; this reinforces the one-replica constraint
  on `jobhunter-worker` (SAD §11 D2).

**Neutral**
- Cron expressions are declared in `Europe/Kyiv`, not UTC, so 07:00 stays 07:00 across DST.

## Links

- SAD: [[../sad]] §6.1, §6.2, §7
- Related: [[0001-modular-monolith-three-deployables]], [[0003-postgresql-efcore-dapper]]
