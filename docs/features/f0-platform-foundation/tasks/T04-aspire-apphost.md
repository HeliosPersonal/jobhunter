# T04 — Aspire AppHost

**Layer:** platform · **Deps:** T03 · **Est:** M · **Owner:** Viacheslav

## What

`JobHunter.AppHost` provisioning PostgreSQL (+pgweb), RabbitMQ (+management), Redis
(+RedisInsight), Typesense and Ollama as containers, declaring the `jobhunterdb` database, and
wiring the three hosts with `WithReference()` / `WaitFor()`. Exactly as documented in
[[../../../engineering/local-development|local development]] §3.

## Done when

- `dotnet run --project src/Aspire/JobHunter.AppHost` brings every resource and host to healthy (AC-01).
- The Aspire dashboard shows live traces, logs and metrics from all three hosts.
- Connection strings are injected by resource name; no host has a hard-coded local endpoint.
- The AppHost is excluded from the coverage gate and from all three Dockerfiles.
- `compose.yaml` provides the same four backing services as a fallback.

## Out of scope

- Production orchestration — Kustomize, T14.

## Links

[[../../../00-overview/adr/0013-aspire-local-dev-only|ADR-0013]] · [[../../../engineering/local-development]]
