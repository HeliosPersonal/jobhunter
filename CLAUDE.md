# JobHunter — project conventions

An AI job-intelligence platform for a single Owner: it discovers engineering jobs from ATS boards,
analyses and ranks them with the Claude Message Batches API against a CV, and delivers one Telegram
digest at 07:00 Europe/Kyiv. It is also, deliberately, a portfolio artifact — the architecture, the
tests and the documentation are part of the deliverable.

**The full design lives in `docs/`.** Start at [READING-GUIDE.md](READING-GUIDE.md).
The canonical vocabulary is [docs/CONTEXT.md](docs/CONTEXT.md) — use those words and only those words.

---

## Hard rules

- **Test coverage MUST be > 90%** (line and branch), CI-enforced by a dedicated *Enforce coverage
  gate* step in `.github/workflows/ci-cd.yml`: it merges the per-assembly XPlat cobertura reports with
  ReportGenerator and fails the build if line or branch coverage falls below 90%. (The Coverlet
  MSBuild `<Threshold>` in `tests/Directory.Build.props` is a local-only convenience — CI collects
  coverage via `--collect:"XPlat Code Coverage" --settings coverage.runsettings`, so that threshold
  never runs in CI.) The exclusion set is authoritative in `coverage.runsettings`: the Aspire
  `AppHost`, `ServiceDefaults`, `Contracts` and `TestKit` assemblies, all `Migrations/*.cs`, and any
  type marked `[ExcludeFromCodeCoverage]` (which is how the three host `Program.cs` composition roots
  are excluded). Every feature is built test-first.
- **The system never applies to a job.** It never submits a form, never emails a recruiter, never
  impersonates the Owner. `Applied` is a status the Owner sets ([[docs/CONTEXT|invariant 7]]).
- **Every Score, Enrichment and Match carries at least one reason.** An unexplained number never
  reaches the Owner (invariant 4).
- **Every CompanyResearch claim carries a source URL.** An uncited claim is discarded, not shown
  (invariant 5).
- **A Run never exceeds its cost ceiling.** The check happens *before* submission, not after
  (invariant 6). The test for this asserts the LLM client is never called.
- **Delivery is idempotent.** One card, one Run, one chat, one delivery (invariant 8).
- **The CV crosses exactly one boundary** — the F4 match prompt. It appears in no log, no span, no
  index, no notification. This is verified by a sentinel-scan suite, and it is the one rule whose
  violation would be genuinely damaging.
- **Single Owner.** No registration, no tenant column, no role model (invariant 9).
- **Robots, `Retry-After` and per-host rate budgets are honoured.** No anti-bot circumvention
  (invariant 10).
- **Secrets never enter the repository, the image layers, or the logs** (invariant 12).

The full list of twelve invariants is in [docs/CONTEXT.md §3](docs/CONTEXT.md). They are not
aspirations — most are enforced by a database constraint or an automated test, and several documents
reference them by number.

---

## Stack (fixed by ADRs)

- **.NET 10**, C#, `net10.0`, nullable enabled, `TreatWarningsAsErrors`. Central Package Management.
  Solution file is `.slnx`.
- **Three deployables** — `JobHunter.Api`, `JobHunter.Worker`, `JobHunter.Telegram` — hosting **nine
  logical pipeline stages** communicating over RabbitMQ ([ADR-0001](docs/00-overview/adr/0001-modular-monolith-three-deployables.md)).
- **RabbitMQ + Wolverine**, with the EF Core transactional outbox and inbox
  ([ADR-0002](docs/00-overview/adr/0002-rabbitmq-wolverine-transport.md),
  [ADR-0007](docs/00-overview/adr/0007-transactional-outbox.md)).
- **PostgreSQL** as the single store. **EF Core** owns the schema, migrations and aggregate writes;
  **Dapper** owns read models. Dapper never writes
  ([ADR-0003](docs/00-overview/adr/0003-postgresql-efcore-dapper.md)).
- **Hangfire** on PostgreSQL under a `hangfire` schema, for scheduling
  ([ADR-0004](docs/00-overview/adr/0004-hangfire-scheduling.md)).
- **Anthropic Message Batches API**, two-tier cascade (`Cheap` for extraction, `Deep` for judgement),
  with a hard pre-submission cost ceiling
  ([ADR-0005](docs/00-overview/adr/0005-anthropic-message-batches-two-tier-cascade.md)). Ollama on the
  cluster is the fallback tier.
- **Structured output** via tool-use JSON Schema, parsed per item, tolerant of failure
  ([ADR-0006](docs/00-overview/adr/0006-structured-output-contract.md)).
- **Typesense** for search, as a rebuildable projection
  ([ADR-0008](docs/00-overview/adr/0008-typesense-over-postgres-fts.md)).
- **Keycloak OIDC** for the API, chat-id allowlist for the bot
  ([ADR-0014](docs/00-overview/adr/0014-keycloak-api-telegram-allowlist.md)).
- **.NET Aspire** for local development orchestration **only** — never for deployment
  ([ADR-0013](docs/00-overview/adr/0013-aspire-local-dev-only.md)).
- **Deployment**: Kustomize base + overlays, GHCR, GitHub Actions on a self-hosted runner on the
  `helios` k3s cluster ([ADR-0010](docs/00-overview/adr/0010-kustomize-ghcr-selfhosted-runner.md)).
  Secrets from Infisical at runtime ([ADR-0011](docs/00-overview/adr/0011-infisical-secrets.md)).
- **Telemetry**: OpenTelemetry → Grafana Alloy → Grafana Cloud
  ([ADR-0012](docs/00-overview/adr/0012-otlp-alloy-grafana-cloud.md)).

Run locally: `dotnet run --project src/Aspire/JobHunter.AppHost`.

---

## Architecture

Dependency direction, enforced by `JobHunter.ArchitectureTests` and therefore by the build:

```
Api | Worker | Telegram  →  Telegram.Transport  →  Infrastructure | Claude | Scrapers | Search  →  Application  →  Domain
```

`JobHunter.Telegram.Transport` is the shared send-path adapter (referenced by both the Worker and the
Telegram host, referencing Application and Domain) that lets Worker-side scheduled handlers resolve
the notifier and renderers.

`Contracts` is referenced across the solution (directly by Application and Scrapers; hosts get it
transitively) and references nothing. `Domain` references nothing but
`Microsoft.Extensions.*.Abstractions`.

Every external dependency sits behind a port in `Domain/Abstractions` — `IJobSource`,
`ILlmBatchClient`, `INotifier`, `ISearchIndex`, `IResearchFetcher`, `IClock`, `IIdGenerator`. Adding an
ATS provider is a new class in `JobHunter.Scrapers`, not a change to the pipeline. This is also what
makes the 90% coverage gate achievable without a network.

Full module map: [docs/00-overview/sad.md §5](docs/00-overview/sad.md).

---

## Working method

**Docs-first, gated.** No task is started before its feature's PRD, SAD, data model and test plan are
accepted — see [docs/IMPLEMENTATION-READINESS.md](docs/IMPLEMENTATION-READINESS.md). All ten features
are currently past that gate.

**One task, one PR.** Every task in a `tasks/tracker.md` is one reviewable PR, ≤ 500 lines, ≤ 1 day.
Update the tracker row in the same PR.

**Ten build gates** (readiness §2) apply to every task: zero warnings, the coverage gate, migrations
applying on a clean database, an idempotency test for every message handler, the architecture rules,
no secret or CV content in any log, an explicit authorization scope on every endpoint, PR size, and
docs updated when behaviour changes.

Branch naming: `feature/{FEATURE}-{TASK}-{kebab-description}`, e.g.
`feature/F3-T05-anthropic-batch-client`.

---

## Testing conventions

- **xUnit + NSubstitute + Shouldly**, with Coverlet for coverage. No Moq.
- **Fixture-driven for everything external.** Five ATS adapters, four LLM output types and every
  Telegram layout are tested against recorded payloads with **zero network**. A payload shape that ever
  caused a production failure is added as a fixture before the fix is merged.
- **Testcontainers** (`postgres:17-alpine`, plus RabbitMQ for messaging tests) for integration.
  `TestDatabase` gives each test its own database and applies migrations on create — which is how the
  migrations gate is satisfied by every integration test rather than by a separate ritual.
- **`FakeClock` and `SequentialIdGenerator`** in `JobHunter.TestKit`. No test waits on real time, and
  no test depends on the real date.
- **Named suites that carry a feature's credibility**, and must stay green:
  - F2 — the **dedup corpus**: zero false merges, or the build fails.
  - F3 — the **crash matrix**: eight kill points, each asserting exactly one batch submission.
  - F4 — the **CV leakage scan** (sentinel tokens, no allowlist) and the **golden ranking set**.
  - F5 — the **rendering corpus** and the **duplicate-delivery suite**.
  - F7 — the **synthetic-behaviour corpus**, including the indifferent profile that must produce *no*
    weights.
  - F8 — the **uncited-claim** and **SSRF** suites.
  - F9 — the **index scan**, **rebuild equivalence** and **endpoint-convention** suites.
- **Live API tests are opt-in**, excluded from the PR suite, and run weekly or nightly as alert-only.
  A provider changing its API is news, not a regression in our code.

Full detail: [docs/engineering/testing-strategy.md](docs/engineering/testing-strategy.md).

---

## Code style

Full rules in [docs/engineering/coding-standards.md](docs/engineering/coding-standards.md). The ones
most often got wrong:

- Expected business outcomes are **values** (`Result<T>`, outcome enums). Exceptions are for programmer
  error and infrastructure faults. A `catch` with an empty body fails review.
- Options validate at **startup**, via `.Validate().ValidateOnStart()` — never at first use.
- Structured logging only; never string interpolation into a log message.
- `IClock` and `IIdGenerator` everywhere; `DateTime.Now` is banned by an architecture test.
- Money is `numeric(12,2)` with an explicit currency column and `decimal` in C#. Never `double`.
- Enums persist as `text`, never as ordinals.
- One `DependencyInjection.cs` per project, one extension method, marked `[ExcludeFromCodeCoverage]`.

---

## Git

Plain, human-authored commit messages. Conventional subjects (`feat(f3): …`). **No AI attribution
trailers.** English only, in code, comments, logs and documents.
