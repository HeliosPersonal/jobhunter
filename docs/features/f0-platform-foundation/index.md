---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "XL"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f0-platform-foundation, mvp, jobhunter]
---

# F0 · Platform Foundation

> **Feature index (MOC).** Every artifact for this feature, in reading order.

The scaffolding every other feature stands on: the solution, the layers, the database, the message
bus, the scheduler, the telemetry, the container images and the deployment pipeline. F0 ships no
user-visible behaviour and that is deliberate — it exists so that F1 through F9 are each a
day-per-task rather than a week of yak-shaving.

## Reading order

1. [[PRD|PRD]] — what "foundation" means concretely, and how we know it is done
2. [[sad|SAD]] — module boundaries, dependency rules, host composition
3. [[data-model|Data model]] — the tables F0 owns (none of them domain tables)
4. [[test-plan|Test plan]] — the harness every later feature reuses
5. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 16 tasks

## Architecture decisions

F0 implements decisions rather than making them. The ones it realises:
[[../../00-overview/adr/0001-modular-monolith-three-deployables|0001]] ·
[[../../00-overview/adr/0002-rabbitmq-wolverine-transport|0002]] ·
[[../../00-overview/adr/0003-postgresql-efcore-dapper|0003]] ·
[[../../00-overview/adr/0004-hangfire-scheduling|0004]] ·
[[../../00-overview/adr/0007-transactional-outbox|0007]] ·
[[../../00-overview/adr/0010-kustomize-ghcr-selfhosted-runner|0010]] ·
[[../../00-overview/adr/0011-infisical-secrets|0011]] ·
[[../../00-overview/adr/0012-otlp-alloy-grafana-cloud|0012]] ·
[[../../00-overview/adr/0013-aspire-local-dev-only|0013]] ·
[[../../00-overview/adr/0015-uuidv7-keys-and-timestamptz|0015]]

## Milestone

M1 — Skeleton. Exit criterion: `dotnet run` via Aspire brings the whole system up, one green
integration test runs against a real PostgreSQL, and CI deploys three images to `apps-staging`.

## Related

[[../../CONTEXT]] · [[../../00-overview/sad]] · [[../../engineering/local-development]] ·
[[../../engineering/ci-cd]] · [[../f1-ats-job-discovery/index|F1 →]]
