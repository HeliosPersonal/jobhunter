# T03 — ServiceDefaults: OpenTelemetry, health, resilience

**Layer:** platform · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

`JobHunter.ServiceDefaults` with `AddServiceDefaults()` and `MapDefaultEndpoints()`,
following the `overflow` implementation: OTel resource, tracing (ASP.NET Core with health-path
filtering, HttpClient, EF Core with SQL tagging), metrics (ASP.NET Core, HttpClient, Runtime,
Npgsql), logging with scopes, `UseOtlpExporter()` gated on the endpoint being configured, service
discovery, and `AddStandardResilienceHandler()` on all HttpClients.

## Done when

- `/alive`, `/ready` and `/health` behave per [[../../../engineering/observability|observability]] §3.
- `/ready` checks PostgreSQL, RabbitMQ and Redis only — never Anthropic or Typesense.
- No `#if DEBUG` and no environment branching (SAD §10 QG-3).
- With `OTEL_EXPORTER_OTLP_ENDPOINT` unset the exporter is not registered and startup succeeds.
- With the endpoint set but unreachable, requests complete normally (AC-06).

## Out of scope

- Domain metrics — T11.
- Dashboards — [[../../../engineering/observability]] §5.

## Links

[[../../../engineering/observability]] · [[../../../00-overview/adr/0012-otlp-alloy-grafana-cloud|ADR-0012]]
