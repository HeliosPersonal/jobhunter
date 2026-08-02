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

# 0013 — .NET Aspire for local development orchestration only

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Running the system locally means PostgreSQL, RabbitMQ, Redis, Typesense and three .NET hosts, wired
together with the right connection strings. Doing that by hand — or by a hand-maintained
`compose.yaml` plus a `.env` file — is a recurring tax and the single biggest barrier to "clone and
run". Both `wisewizard` and `overflow` adopted Aspire for exactly this, and both deliberately kept
it out of production.

## Decision drivers

- `dotnet run --project src/Aspire/JobHunter.AppHost` must be the entire local setup.
- The Aspire dashboard gives traces, logs and metrics locally with no extra configuration.
- Production deployment is already decided and is Kustomize
  ([[0010-kustomize-ghcr-selfhosted-runner|ADR-0010]]); two deployment mechanisms would be one too many.
- `ServiceDefaults` is genuinely valuable in production even though the AppHost is not.

## Considered options

1. **`compose.yaml` + a README of manual steps.**
2. **Aspire for local dev; Kustomize for deployment.**
3. **Aspire end-to-end, generating production manifests.**

## Decision outcome

**Chosen: Option 2.**

- `src/Aspire/JobHunter.AppHost` provisions PostgreSQL (with pgweb), RabbitMQ (with the management
  UI), Redis (with RedisInsight) and Typesense as containers, declares the `jobhunter` database,
  and wires the three hosts with `WithReference()` / `WaitFor()`. It also runs an Ollama container
  for the offline cheap-tier path ([[0005-anthropic-message-batches-two-tier-cascade|ADR-0005]]).
- `src/Aspire/JobHunter.ServiceDefaults` **is** production code: it carries the OpenTelemetry wiring,
  `/alive` and `/ready` health endpoints, service discovery and the standard HTTP resilience handler.
  Every host references it.
- The AppHost is excluded from the Docker images and from the coverage gate. Nothing in
  `Application`, `Domain` or `Infrastructure` may reference it — enforced by an architecture test.
- A `compose.yaml` is retained as a fallback for contributors without the Aspire workload, but it is
  not the documented path.

## Consequences

**Positive**
- Clone → `dotnet run` → a fully wired system with a telemetry dashboard, in one command.
- No hand-maintained local connection strings; Aspire injects them by resource name.
- The same OTLP code path is exercised locally and in production, only the endpoint differs.

**Negative**
- An Aspire workload is required for the primary local path.
- Two orchestration descriptions exist (AppHost and Kustomize) and can drift. Bounded by the fact
  that the AppHost describes only dependencies, never replicas, ingress or scaling.

**Neutral**
- If Aspire's deployment story matures, adopting it is additive rather than a rewrite.

## Links

- SAD: [[../sad]] §5
- Engineering: [[../../engineering/local-development]]
- Related: [[0010-kustomize-ghcr-selfhosted-runner]], [[0012-otlp-alloy-grafana-cloud]]
