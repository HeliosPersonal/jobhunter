---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "XL"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f0-platform-foundation, mvp, jobhunter]
---

# Test plan — f0-platform-foundation

> F0's tests are mostly *harness*: the fixtures every later feature reuses. Getting them right here
> is why F1–F9 can hit the coverage gate without inventing infrastructure.

## Levels

| Level | Scope | Tooling |
|---|---|---|
| Unit | `Result<T>`, `SystemClock`, `IIdGenerator` ordering, options validation | xUnit |
| Integration | `TestDatabase` harness, migration application, outbox/inbox behaviour | xUnit + Testcontainers (Postgres) |
| Messaging | Handler chain, transactional publish, redelivery, dead-lettering | xUnit + Testcontainers (Postgres + RabbitMQ) |
| Architecture | Every rule in [[sad]] §10 QG-2 | xUnit + NetArchTest |
| Smoke | Health endpoints, telemetry emission, startup failure on bad config | `WebApplicationFactory` |
| Manifest | `kubectl kustomize` renders both overlays | CI shell step |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `AppHost_StartsAllResources_AndHostsReportHealthy` (manual checklist + `docs/engineering/local-development` §2) | Smoke |
| AC-02 | `Migrations_ApplyCleanly_OnEmptyDatabase` | Integration |
| AC-03 | `Handler_WhenTransactionRollsBack_PublishesNothing` | Messaging |
| AC-04 | `Handler_WhenMessageRedelivered_ProducesSingleEffect` | Messaging |
| AC-05 | `PipelineScope_PropagatesCorrelationId_AcrossStages` | Messaging |
| AC-06 | `Telemetry_WhenCollectorUnreachable_DoesNotBlockOrFailHealth` | Smoke |
| AC-07 | CI pipeline itself; asserted by a green run to staging | CI |
| AC-08 | `Domain_HasNoDependencyOn_Infrastructure` + 5 sibling rules | Architecture |
| AC-09 | `Startup_WithMissingRequiredOption_FailsFastNamingTheKey` | Smoke |
| AC-10 | `OperationalEndpoint_WithoutCredentials_IsRefused` and `LivenessAndReadiness_AreAnonymous` | Smoke |
| AC-11 | `MigrationJob_CompletesBefore_DeploymentsBecomeReady` (manifest ordering + integration) | Manifest + Integration |

## Architecture tests — one per rule

```csharp
[Fact]
public void Domain_HasNoDependencyOn_Infrastructure() =>
    Types.InAssembly(typeof(IClock).Assembly)
         .ShouldNot().HaveDependencyOnAny(
             "JobHunter.Infrastructure", "JobHunter.Application",
             "Microsoft.EntityFrameworkCore", "Wolverine", "Npgsql")
         .GetResult().IsSuccessful.Should().BeTrue();

[Fact]
public void DapperQueries_NeverWrite() =>
    Types.InNamespace("JobHunter.Infrastructure.Persistence.Queries")
         .Should().NotCallAnyOf("ExecuteAsync", "Execute", "ExecuteScalar")
         .GetResult().IsSuccessful.Should().BeTrue();

[Fact]
public void NoTypeUses_AmbientClock() =>
    SourceScan.ForPattern(@"DateTime(Offset)?\.(Now|UtcNow)")
              .ExcludingType<SystemClock>()
              .Matches.Should().BeEmpty();
```

**Each architecture test ships with a deliberately violating fixture in a `~Violations` folder,
excluded from the build, proving the test can fail.** An assertion that has never gone red is an
assertion nobody has verified.

## Edge cases / error paths

- Migration applied twice → second application is a no-op; `__EFMigrationsHistory` unchanged.
- RabbitMQ unavailable at startup → the host still becomes live; `/ready` reports not-ready; the
  outbox accumulates; recovery drains it without loss.
- Infisical unreachable in Development → skipped entirely, startup succeeds.
- Infisical unreachable in Production → startup fails with a non-zero exit.
- Two workers started accidentally → Hangfire's distributed lock ensures one recurring-job owner;
  asserted so the constraint is documented rather than assumed.
- Telemetry endpoint set to an unreachable address → work proceeds, exporter drops silently.
- `kubectl kustomize` with an unsubstituted placeholder → renders `SHA_REPLACED_BY_CICD`, caught by
  the CI preview step before apply.

## Test data

- No domain fixtures — F0 has no domain.
- `TestDatabase` creates one uniquely-named database per test, applies migrations, drops with
  `FORCE` on dispose. One container per test run, gated by a semaphore.
- `FakeClock` and `SequentialIdGenerator` live in `JobHunter.TestKit`, referenced by every later
  test project, so time and identity are deterministic everywhere.

## NFR validation

- Cold start < 90 s → timed manually at F0 completion, recorded in [[../../engineering/local-development]].
- PR pipeline < 8 min → GitHub Actions duration, reviewed weekly.
- Test suite < 5 min → `dotnet test` wall clock in CI; a regression is a task, not a shrug.
- Coverage > 90% → Coverlet threshold fails the build.
- Migration < 5 s → asserted in `Migrations_ApplyCleanly_OnEmptyDatabase`.

## CI

- **PR:** unit + integration + messaging + architecture + smoke, coverage gate, manifest render.
- **Push to `develop`:** the above, then build, push, terraform, deploy staging, rollout status.
- **Push to `main`:** blocked until F5 — production is gated on there being a product to deploy.

## Related

[[../../engineering/testing-strategy]] · [[sad]] §10 · [[../../IMPLEMENTATION-READINESS]] §2
