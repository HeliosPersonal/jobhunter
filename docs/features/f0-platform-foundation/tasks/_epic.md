---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "XL"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f0-platform-foundation, mvp, jobhunter]
---

# Epic — F0 Platform Foundation

The scaffolding every other feature stands on: solution layout and build rules, domain primitives,
persistence with migrations, messaging with a transactional outbox, scheduling, telemetry with
correlation, configuration and secrets, the test harness, architecture enforcement, container images
and the deployment pipeline.

F0 delivers no user-visible behaviour. Its success criterion is negative: **F1 through F9 must not
have to touch anything F0 built.**

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-07, AC-01…AC-11, NFRs
- SAD: [[../sad|sad]] — module map, dependency rule, startup and publish flows
- Data model: [[../data-model|data-model]] — framework tables and migration conventions
- Test plan: [[../test-plan|test-plan]] — the harness later features reuse
- System SAD: [[../../../00-overview/sad|SAD]] §5, §8
- ADRs realised: [[../../../00-overview/adr/0001-modular-monolith-three-deployables|0001]],
  [[../../../00-overview/adr/0002-rabbitmq-wolverine-transport|0002]],
  [[../../../00-overview/adr/0003-postgresql-efcore-dapper|0003]],
  [[../../../00-overview/adr/0004-hangfire-scheduling|0004]],
  [[../../../00-overview/adr/0007-transactional-outbox|0007]],
  [[../../../00-overview/adr/0010-kustomize-ghcr-selfhosted-runner|0010]],
  [[../../../00-overview/adr/0011-infisical-secrets|0011]],
  [[../../../00-overview/adr/0012-otlp-alloy-grafana-cloud|0012]],
  [[../../../00-overview/adr/0013-aspire-local-dev-only|0013]],
  [[../../../00-overview/adr/0015-uuidv7-keys-and-timestamptz|0015]]

## Scope

**In:** everything in [[../sad|sad]] §3 "In".
**Out:** every domain table, every external business adapter, every prompt, production deployment.

## Module scope

`Domain/Common`, `Domain/Abstractions`, `Application/Common`, `Infrastructure/{Persistence,Messaging,Caching,Http,Configuration}`,
`Aspire/{AppHost,ServiceDefaults}`, all three host `Program.cs`, `k8s/`, `terraform/`, `.github/workflows/`,
all four test projects.

## Handoff interfaces

Consumed by every later feature:

| Interface | Consumer |
|---|---|
| `IClock`, `IIdGenerator`, `Result<T>` | all |
| `JobHunterDbContext` + configuration convention | F1–F8 |
| `NpgsqlConnectionFactory` + query convention | F5, F7, F9 |
| Wolverine handler discovery + `[Transactional]` | F1–F8 |
| `RecurringJobRegistry` | F1, F3, F7 |
| `Telemetry.Source` / `Telemetry.Meter` | all |
| `TestDatabase`, `FakeClock`, `SequentialIdGenerator` | all test projects |

## Tasks

See [[tracker|tracker]]. The 14-task baseline is **8.0** person-days; with T15 (backup job) and T16
(replay-dlq CLI) added the tracker holds 16 tasks at ≈ 9.0 person-days.

## Definition of Done (epic)

- AC-01…AC-11 covered by passing tests ([[../test-plan|test-plan]]).
- `dotnet run --project src/Aspire/JobHunter.AppHost` brings the whole system up on a clean clone.
- A push to `develop` reaches `apps-staging` with no human action.
- All eight architecture rules are asserted and each has a proven-red violating fixture.
- Coverage gate green at > 90% line and branch.
- Milestone M1 exit criterion in [[../../../BACKLOG|BACKLOG]] §1 satisfied.
