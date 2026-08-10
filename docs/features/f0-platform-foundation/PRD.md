---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "XL"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f0-platform-foundation, mvp, jobhunter]
---

# PRD — f0-platform-foundation

> **Inputs (required):** [[../../00-overview/idea-brief|idea-brief]] · [[../../CONTEXT]] · [[../../00-overview/sad|SAD]]
> **External context:** [[../../DECISION-LOG]] (D8, D9), [[../../IMPLEMENTATION-READINESS]] §2

## 1. Context

Nine features follow this one, each needing the same things: a place to put an aggregate, a way to
migrate a schema, a way to publish an event atomically with a state change, a way to schedule
something, a way to see what happened, and a way to get it to the cluster. Building those nine times
is how a solo project runs out of weeks.

F0 also fixes the shape of the repository. [[../../00-overview/sad|SAD]] §5 declares a dependency
direction; unless something enforces it on day one, it will be violated by week three and the
violation will be discovered during a refactor nobody has time for. The architecture test in T12 is
therefore part of the foundation, not a nicety.

The feature is deliberately unglamorous and deliberately complete. Half a foundation — say, EF Core
without the outbox, or telemetry without correlation — is worse than none, because the missing half
gets bolted on under time pressure during F3.

## 2. Goals

- A developer clones the repository and has the entire system running with one command
  ([[../../engineering/local-development]]).
- A feature author can add an aggregate, a migration, a handler and a scheduled job without touching
  wiring.
- A state change and its event commit atomically, from the first handler ever written
  ([[../../00-overview/adr/0007-transactional-outbox|ADR-0007]]).
- Every log, metric and trace is correlated by `run_id` from the first line of pipeline code.
- A commit on `develop` reaches `apps-staging` without a human running a command.
- The architecture rules of [[../../00-overview/sad|SAD]] §5 fail the build when broken.

## 3. Non-goals

- Any domain behaviour. F0 creates no `companies`, `jobs`, `runs` or any other domain table.
- Anthropic integration — F3 owns `ILlmBatchClient` and its adapter.
- Telegram integration beyond an empty host that starts and reports healthy — F5 owns the bot.
- Typesense integration — F9 owns it. F0 only makes the connection settings available.
- Production deployment. F0 must reach staging; production is gated on F5.

## 4. User stories

### US-01: Run the whole system locally with one command
**As the** developer **I want** a single command that starts the application and every backing
service **so that** I can start working on a feature within a minute of cloning.

### US-02: Add a table without inventing a process
**As the** developer **I want** a DbContext, a configuration convention and a working migration
pipeline **so that** adding an aggregate is one class plus one generated migration.

### US-03: Publish an event without a dual-write bug
**As the** developer **I want** publishing to be transactional with the state change by default
**so that** I cannot accidentally create the failure mode that loses a day's pipeline.

### US-04: See what the system is doing
**As the** operator **I want** correlated logs, metrics and traces flowing to Grafana from the first
deploy **so that** diagnosing a stuck Run is reading a dashboard rather than adding instrumentation.

### US-05: Ship without ceremony
**As the** developer **I want** a push to `develop` to build, test, containerise and deploy to
staging **so that** deployment is never the reason a change waits.

### US-06: Be told when I break the architecture
**As the** developer **I want** the build to fail when a dependency rule or a persistence rule is
violated **so that** the design in the SAD stays true without me policing it.

### US-07: Start safely or not at all
**As the** operator **I want** the application to refuse to start with invalid or missing
configuration **so that** a misconfiguration is a failed rollout at 14:00 rather than a failed Run
at 02:00.

## 5. Acceptance criteria

### AC-01 (US-01) — happy path
**Given** a clean clone on a machine with the .NET SDK, the Aspire workload and Docker
**When** the developer runs the documented single command
**Then** PostgreSQL, RabbitMQ, Redis and Typesense start, the schema is applied, all three
application hosts report healthy, and a dashboard shows their live telemetry.

### AC-02 (US-02) — happy path
**Given** a new aggregate with an entity configuration
**When** the developer generates a migration and starts the system
**Then** the migration applies to an empty database, the table exists with the declared keys and
indexes, and the same migration applies unchanged in the integration-test harness.

### AC-03 (US-03) — domain invariant
**Given** a handler that changes state and publishes an event
**When** the transaction rolls back after the publish call
**Then** neither the state change nor the event is observable to any consumer.

### AC-04 (US-03) — domain invariant
**Given** a handler whose message is delivered twice
**When** the handler runs both times
**Then** exactly one effect is observable, and the duplicate delivery is recorded rather than
silently ignored.

### AC-05 (US-04) — cross-context
**Given** a unit of pipeline work carrying a correlation identifier
**When** it executes across two stages
**Then** every log line and every span from both stages carries that identifier, and they resolve to
a single end-to-end trace.

### AC-06 (US-04) — error path
**Given** the telemetry collector is unreachable
**When** the application runs
**Then** work completes normally and telemetry is dropped without blocking, retrying indefinitely or
failing a health check.

### AC-07 (US-05) — happy path
**Given** a commit merged to the staging branch that passes all checks
**When** the pipeline runs
**Then** three images are published tagged with the commit, the schema is migrated, all three
deployments roll out successfully, and no human ran a command.

### AC-08 (US-06) — domain invariant
**Given** a change that makes the domain project depend on an infrastructure concern
**When** the test suite runs
**Then** the build fails naming the rule that was broken.

### AC-09 (US-07) — error path
**Given** a required configuration value is absent or invalid
**When** the application starts
**Then** it fails immediately with a message naming the missing key, and never reports ready.

### AC-10 (US-07) — authorization
**Given** an unauthenticated request to an operational endpoint that exposes system state
**When** the request is made
**Then** it is refused, while the liveness and readiness endpoints remain reachable without
credentials.

### AC-11 (US-02) — cross-context
**Given** the deployment applies a schema change
**When** the new version rolls out
**Then** the schema change completes before any application instance serving traffic uses it, and no
two instances attempt it concurrently.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Cold start (local, cached images) | < 90 s from command to all hosts healthy | Timed on the reference machine |
| Cold start (cluster) | < 45 s from pod scheduled to `ready` | `kubectl get pods -w` |
| PR pipeline duration | < 8 min from push to green | GitHub Actions duration |
| Test suite duration | < 5 min for the full hermetic suite | `dotnet test` wall clock |
| Coverage | > 90% line and branch, gate green | CI *Enforce coverage gate* step (Coverlet `<Threshold>` is local-only) |
| Migration application | < 5 s on an empty database | Integration harness timing |
| Memory at idle | < 200 MB per host | Container metrics |

## 6.1 Security / privacy

- **Data classification:** none — F0 stores no domain or personal data.
- **Personal data touched:** none.
- **AuthZ/AuthN impact:** establishes the fallback-deny authorization policy every later endpoint
  inherits, and the anonymous exemption for `/alive` and `/ready`
  ([[../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]]).
- **Abuse cases:**
  - Operational endpoints exposed publicly → ingress restricted to `/api`; the scheduler dashboard is
    port-forward only and scope-gated (AC-10).
  - Secrets in image layers or logs → Infisical at runtime, placeholders in git, redaction tests (gate G6).
  - A pod starting with empty credentials → hard startup failure in Production (AC-09).
- **Security review:** N/A — no data, no external surface beyond an authenticated health endpoint.

## 7. Metrics / KPIs

- **Time from clone to running system** — baseline: n/a, target: < 5 min including image pulls.
- **PR pipeline green rate** — target: > 95% of PRs pass first time (a flaky foundation poisons every later feature).
- **Time from merge to staging** — target: < 10 min.
- **Wiring changes required per later feature** — target: 0 changes to F0 files when adding F1–F9 handlers.

## 8. Open questions

- [ ] Does the API expose the scheduler dashboard behind the admin scope, or stay port-forward only?
  — owner: Viacheslav — *default now: port-forward only; revisit if remote debugging becomes routine.*
- [ ] Is the `compose.yaml` fallback maintained, or documented as best-effort?
  — owner: Viacheslav — *default now: best-effort, tested at F0 completion and not after.*

## DoD self-check

- [x] §5 has ≥1 AC of each coverage type: happy (01, 02, 07), error (06, 09), authorization (10), domain invariant (03, 04, 08), cross-context (05, 11)
- [x] §5 AC contain no HTTP verbs, URL paths, status codes, class names, JSON or SQL
- [x] Every US has ≥1 AC; every AC names its US
- [x] NFRs are measurable
