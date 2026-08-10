---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "15"
ticket: ""
tags: [engineering, testing, jobhunter]
---

# Testing strategy

> The gate is >90% line **and** branch coverage, CI-enforced ([[../DECISION-LOG|D9]]).
> The gate is not the goal — it is the floor that keeps refactoring safe on a part-time solo project.

---

## 1. Levels

| Level | Scope | Network | Docker | Tooling |
|---|---|---|---|---|
| **Unit** | Domain logic, normalisation, fingerprinting, ranking arithmetic, cost accounting, prompt building, output parsing | No | No | xUnit + NSubstitute |
| **Fixture** | Every external-payload parser — five ATS adapters, four LLM output types, Telegram callbacks — against recorded responses | No | No | xUnit + committed JSON fixtures |
| **Integration** | Repositories, EF migrations, Dapper queries, outbox behaviour, idempotency constraints | No | **Yes** | xUnit + Testcontainers (`postgres:17-alpine`) |
| **Messaging** | Handler chains end to end, including redelivery and dead-lettering | No | Yes | Testcontainers (Postgres + RabbitMQ) |
| **Contract** | ATS responses still match the shape the adapter consumes | **Yes** | No | Opt-in, excluded from PR CI, run weekly |
| **Golden set** | 50 hand-labelled jobs scored against recorded model output; ranking regression | No | No | xUnit + labelled corpus |
| **Live drift** | 10 items through the real model, compared to fixtures | Yes | No | Nightly job, alert-only, never gates a PR |

The PR suite is **Unit + Fixture + Integration + Messaging + Golden set**: fully hermetic apart from
Docker, and it must run in under five minutes or it stops being run.

---

## 2. Coverage gate

`tests/Directory.Build.props`:

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <CollectCoverage>true</CollectCoverage>
    <Threshold>90</Threshold>
    <ThresholdType>line,branch</ThresholdType>
    <ThresholdStat>total</ThresholdStat>
    <ExcludeByAttribute>ExcludeFromCodeCoverage,GeneratedCodeAttribute</ExcludeByAttribute>
    <Exclude>[JobHunter.Api]*Program*,[JobHunter.Worker]*Program*,[JobHunter.Telegram]*Program*,[JobHunter.AppHost]*,[JobHunter.ServiceDefaults]*,[JobHunter.Contracts]*</Exclude>
  </PropertyGroup>
</Project>
```

Excluded, with reason: composition roots (verified by the system starting), the Aspire AppHost
(local-dev only), `ServiceDefaults` (third-party wiring), and `Contracts` (records with no behaviour).

Each test project narrows `<Include>` to its own module, so `JobHunter.Domain.Tests` cannot inflate
its number by exercising `Infrastructure`.

---

## 3. Integration test harness

One PostgreSQL container per test run, one isolated database per test — the `wisewizard` pattern.

```csharp
public sealed class TestDatabase : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = null!;
    private string _databaseName = null!;

    public static async Task<TestDatabase> CreateAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _container ??= await StartContainerAsync();
        }
        finally { Gate.Release(); }

        var db = new TestDatabase { _databaseName = $"jh_{Guid.CreateVersion7():N}" };
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"""CREATE DATABASE "{db._databaseName}";""";
            await cmd.ExecuteNonQueryAsync();
        }

        db.ConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = db._databaseName
        }.ConnectionString;

        await using var ctx = JobHunterDbContextFactory.Create(db.ConnectionString);
        await ctx.Database.MigrateAsync();   // proves every migration applies on a clean DB (gate G3)
        return db;
    }

    private static Task<PostgreSqlContainer> StartContainerAsync() { /* ... */ }

    public async ValueTask DisposeAsync() { /* DROP DATABASE ... WITH (FORCE) */ }
}
```

Applying migrations in the harness means **gate G3 is satisfied by every integration test**, not by a
separate ritual nobody remembers to run.

---

## 4. What must be tested, per feature

Non-negotiable, checked at review:

| Concern | Required test |
|---|---|
| Every message handler | Runs twice, produces one effect (gate G4) |
| Every unique constraint that encodes an invariant | A test that violates it and asserts the failure |
| Every external payload parser | ≥1 happy fixture, ≥1 malformed fixture, ≥1 empty fixture |
| Every LLM output type | Schema-valid, schema-invalid, partially-valid, and empty-array cases |
| Every EF migration | Applies on a clean database (implicit via the harness) |
| Every value object | Equality, normalisation, and the invalid-input path |
| Time-dependent logic | Driven by a `FakeClock`, never by `Task.Delay` |
| Cost enforcement | A Run seeded above the ceiling aborts **without calling the client** |

---

## 4a. Shared artifact-scanner harness

Four suites assert the same shape of property — "no secret, CV, note or prompt content appears in a
given artifact stream" — and they must not drift into four different scanners. They share **one**
harness in `JobHunter.TestKit`, `ArtifactScanner`, and each suite supplies only its sentinel set and
its artifact source:

| Consumer | Artifact stream scanned | Sentinels |
|---|---|---|
| Gate **G6** (`SecretRedactionTests`) | log sink + span exporter output | API keys, connection strings, Telegram tokens |
| **F4 T10** (CV leakage scan) | log sink, span exporter, Typesense index, error payloads | injected CV sentinel tokens, **no allowlist** |
| **F6 T07** (note-content scan) | log sink + span exporter | application-note sentinels |
| **F10 audit** | command-invocation logs | argument-value sentinels |

The harness takes `(IEnumerable<string> sentinels, IArtifactSource source)` and fails on any
substring match; a new scan is a new sentinel set plus a source, never a new scanner. This keeps the
"no allowlist" discipline of F4 T10 in one place rather than re-implemented per feature.

---

## 5. LLM testing

Model output cannot be asserted for correctness, so it is bounded instead:

1. **Prompt building is a pure function** — assert the exact rendered string against a snapshot. A
   prompt change is visible in a diff, which is the whole point of `PromptVersion`.
2. **Parsing is tested against fixtures**, including the failure modes actually observed: truncated
   JSON, a `score` of `"95"` as a string, `reasons: []`, an unknown enum value, a null where the
   schema says required. Each must degrade to a recorded failure, never throw.
3. **The golden set** — 50 hand-labelled jobs with expected score *bands* (not exact scores) and
   expected ordering of the top 5. It gates ranking changes: a refactor that reorders the top 5
   fails until the labels are updated deliberately.
4. **Live drift** runs nightly outside CI, comparing real model output to fixtures on 10 items and
   alerting on divergence. It never blocks a PR — a provider-side change is information, not a build break.

---

## 6. What is deliberately not tested

Recorded so it is not mistaken for an oversight:

- **Aspire AppHost wiring** — it either starts or it does not, and a test would assert the framework.
- **Generated EF migration bodies** — the harness proves they apply; asserting their contents tests EF.
- **Telegram's rendering of MarkdownV2** — escaping *is* tested; how Telegram draws it is not ours.
- **Live Anthropic quality** — unassertable. Bounded by fixtures and the golden set instead.
- **Kubernetes manifests** — validated by `kubectl kustomize` in CI, not by unit tests.

---

## 7. CI wiring

| Trigger | Suites |
|---|---|
| Pull request | Unit, Fixture, Integration, Messaging, Golden set + architecture tests + coverage gate |
| Push to `develop` | The above, then build and deploy to staging |
| Push to `main` | The above, then build, deploy to production, smoke test — **planned, not yet implemented (F5-gated)**; only the `develop` → staging path exists today |
| Weekly | Contract tests against live ATS endpoints, and the F4 T21 live cost/cache measurement (alert-only) |
| Nightly | Live model drift (alert-only) |

The weekly cost/cache run (`LiveAnthropicCostAndCacheTests`, F4 T21) is gated on `ANTHROPIC_API_KEY`:
it submits a real 20-item matching batch, confirms the CV prompt cache actually hits against the live
API and that the measured cost lands at or under the ceiling, so the ~$1.03/day figure in
[[../operations/infrastructure]] §8 is verified rather than asserted. Absent the key it skips, so it
never runs — or bills — in the PR suite.

---

## Related

- [[coding-standards]] · [[ci-cd]] · [[../IMPLEMENTATION-READINESS]] §2
- [[../00-overview/sad]] §10
