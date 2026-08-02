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

# 0012 — OTLP → Grafana Alloy → Grafana Cloud for all telemetry

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The system is a pipeline whose most important failure mode is *silent*: a stage that stops
publishing, a Batch that never completes, a source quietly returning zero jobs. The operator is one
person who will look at a dashboard once a day, if that. Telemetry must therefore be push-based,
centralised, and already wired before the first feature ships. Helios runs Grafana Alloy in the
`monitoring` namespace, forwarding to Grafana Cloud.

## Decision drivers

- Zero new infrastructure — Alloy and the Grafana Cloud account already exist.
- One correlated view: a trace, its logs and its metrics must share `run_id` and `job_id`.
- `.NET` first-class support: OpenTelemetry for ASP.NET Core, HttpClient, Runtime, EF Core and Npgsql.
- The helios convention explicitly forbids pod-log tailing for .NET services (it duplicates the OTLP log stream).

## Considered options

1. **Serilog to files plus `kubectl logs`.**
2. **Self-hosted Prometheus + Loki + Tempo + Grafana in-cluster.**
3. **OTLP export to the existing Grafana Alloy, forwarded to Grafana Cloud.**
4. **A commercial APM agent.**

## Decision outcome

**Chosen: Option 3.**

Every host calls `AddServiceDefaults()` from `JobHunter.ServiceDefaults`, which configures the
OpenTelemetry resource (`service.name`, `service.version`, `deployment.environment`), traces
(ASP.NET Core with health-endpoint filtering, HttpClient, EF Core with SQL tagging), metrics
(ASP.NET Core, HttpClient, Runtime, Npgsql) and logs, all exported over OTLP to
`grafana-alloy.monitoring.svc.cluster.local:4318`. Pod-log tailing stays off for these services.

Domain-specific instrumentation is defined once in `JobHunter.Application/Common/Telemetry.cs`:
one `ActivitySource` per stage, and the metrics named in SAD §7 —
`jobhunter.run.duration`, `jobhunter.run.cost_usd`, `jobhunter.jobs.discovered`,
`jobhunter.jobs.deduplicated`, `jobhunter.batch.latency`, `jobhunter.digest.cards`,
`jobhunter.source.failures`. `run_id` and `job_id` are on every pipeline log and span.
CV text, prompt bodies and secrets are never emitted (invariant 12, C18).

## Consequences

**Positive**
- Full correlation across metrics, logs and traces from the first deploy; no retrofit.
- Grafana Cloud handles storage, retention and alerting — nothing to operate in-cluster.
- Identical wiring to `overflow`, so its dashboards import with only a service-name change.

**Negative**
- Telemetry leaves the cluster to a SaaS. Acceptable: no personal data is in it, by construction.
- Grafana Cloud free-tier limits will eventually bite; mitigated by dropping `/health` spans and
  keeping metric cardinality low (no `job_id` as a metric label — it is a span attribute only).

**Neutral**
- Local development sends OTLP to the Aspire dashboard instead, via the same environment variables.

## Links

- SAD: [[../sad]] §7
- Engineering: [[../../engineering/observability]]
- Infrastructure: [[../../operations/infrastructure]]
