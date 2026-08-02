---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "index"
ticket: ""
tags: [index, moc, jobhunter]
---

# JobHunter — documentation index

> **Map of content.** Every document in this repository, grouped by what you came here to do.
> If you are reading this repository for the first time, start with [[../READING-GUIDE|READING-GUIDE]] instead.

---

## Orientation

| Document | When to read it |
|---|---|
| [[../README\|README]] | The elevator pitch and how to run it |
| [[../READING-GUIDE\|READING-GUIDE]] | How this documentation is organised and in what order to read it |
| [[CONTEXT]] | **The canonical vocabulary and the twelve invariants.** Read this before anything else |
| [[00-overview/idea-brief\|Idea brief]] | Why this project exists, which approach was chosen, what was rejected |
| [[00-overview/sad\|System Architecture Document]] | How it is built, in twelve arc42 sections |

## Cross-cutting living documents

| Document | Purpose |
|---|---|
| [[BACKLOG]] | Mission control — milestones, feature order, post-MVP, open decisions |
| [[DECISION-LOG]] | Eleven cross-cutting product and process decisions, with what was rejected and why |
| [[ARCHITECTURE-OPEN-DECISIONS]] | Twelve decisions not yet made, ranked by blast radius |
| [[IMPLEMENTATION-READINESS]] | The gate between documentation and code: artifact matrix, ten build gates, build order |
| [[DECISIONS-MATRIX.uk\|Матриця рішень]] 🇺🇦 | **Reconfiguration control panel** — every decision as a 3–4 option menu with the chosen one marked, its blast radius and the cost of switching. Ukrainian |

## Architecture

| Document | Purpose |
|---|---|
| [[00-overview/sad\|SAD]] | System architecture — context, containers, runtime views, quality goals |
| [[architecture/data-model\|Global data model]] | The authoritative schema, ownership map, indexes |
| [[architecture/event-catalog\|Event catalog]] | Every message that crosses a stage boundary |
| [[00-overview/adr/0001-modular-monolith-three-deployables\|ADR-0001]] … [[00-overview/adr/0015-uuidv7-keys-and-timestamptz\|ADR-0015]] | Fifteen system-level architecture decisions |

### System ADRs

| # | Title |
|---|---|
| [[00-overview/adr/0001-modular-monolith-three-deployables\|0001]] | Modular monolith, three deployables, nine stages |
| [[00-overview/adr/0002-rabbitmq-wolverine-transport\|0002]] | RabbitMQ + Wolverine as transport and handler framework |
| [[00-overview/adr/0003-postgresql-efcore-dapper\|0003]] | PostgreSQL single store; EF Core writes, Dapper reads |
| [[00-overview/adr/0004-hangfire-scheduling\|0004]] | Hangfire for scheduling, over Quartz.NET |
| [[00-overview/adr/0005-anthropic-message-batches-two-tier-cascade\|0005]] | Anthropic Message Batches, two-tier model cascade |
| [[00-overview/adr/0006-structured-output-contract\|0006]] | Schema-bound structured output with tolerant parsing |
| [[00-overview/adr/0007-transactional-outbox\|0007]] | EF Core transactional outbox for event publication |
| [[00-overview/adr/0008-typesense-over-postgres-fts\|0008]] | Typesense for job search over PostgreSQL FTS |
| [[00-overview/adr/0009-ats-first-no-linkedin\|0009]] | ATS-first ingestion; LinkedIn and aggregators out of scope |
| [[00-overview/adr/0010-kustomize-ghcr-selfhosted-runner\|0010]] | Kustomize + GHCR + GitHub Actions self-hosted runner |
| [[00-overview/adr/0011-infisical-secrets\|0011]] | Infisical for runtime secrets |
| [[00-overview/adr/0012-otlp-alloy-grafana-cloud\|0012]] | OTLP → Grafana Alloy → Grafana Cloud |
| [[00-overview/adr/0013-aspire-local-dev-only\|0013]] | .NET Aspire for local development only |
| [[00-overview/adr/0014-keycloak-api-telegram-allowlist\|0014]] | Keycloak OIDC for the API; chat-id allowlist for the bot |
| [[00-overview/adr/0015-uuidv7-keys-and-timestamptz\|0015]] | UUID v7 keys, `timestamptz` UTC, `numeric` money |

## Engineering

| Document | When to read it |
|---|---|
| [[engineering/local-development\|Local development]] | Getting the system running on your machine |
| [[engineering/coding-standards\|Coding standards]] | Before writing any code |
| [[engineering/testing-strategy\|Testing strategy]] | Before writing any test |
| [[engineering/ci-cd\|CI/CD]] | How a commit reaches the cluster |
| [[engineering/deployment\|Deployment]] | Manifests, overlays, Terraform, migrations |
| [[engineering/observability\|Observability]] | Instrumentation, metrics, alerts, dashboards |
| [[engineering/security\|Security]] | Data classification, auth, secrets, outbound hygiene |

## Operations

| Document | When to read it |
|---|---|
| [[operations/infrastructure\|Infrastructure]] | The helios cluster and what JobHunter uses of it |
| [[operations/runbooks\|Runbooks]] | Something is broken — R1 through R10 |

## Features

Each feature folder holds `index.md` (its map of content), `PRD.md`, `sad.md`, `data-model.md`,
`test-plan.md`, `tasks/_epic.md`, `tasks/tracker.md`, its task files, and where applicable `adr/` and
`contracts/`.

| # | Feature | Milestone | Tasks | Tracker |
|---|---|---|---|---|
| F0 | [[features/f0-platform-foundation/index\|Platform foundation]] | M1 | 14 | [[features/f0-platform-foundation/tasks/tracker\|tracker]] |
| F1 | [[features/f1-ats-job-discovery/index\|ATS job discovery]] | M2 | 12 | [[features/f1-ats-job-discovery/tasks/tracker\|tracker]] |
| F2 | [[features/f2-normalization-dedup/index\|Normalization & deduplication]] | M2 | 9 | [[features/f2-normalization-dedup/tasks/tracker\|tracker]] |
| F3 | [[features/f3-claude-batch-enrichment/index\|Claude batch enrichment]] | M3 | 13 | [[features/f3-claude-batch-enrichment/tasks/tracker\|tracker]] |
| F4 | [[features/f4-cv-matching-ranking/index\|CV matching & ranking]] | M3 | 11 | [[features/f4-cv-matching-ranking/tasks/tracker\|tracker]] |
| F5 | [[features/f5-daily-digest-telegram/index\|Daily digest & Telegram]] | **M4** | 12 | [[features/f5-daily-digest-telegram/tasks/tracker\|tracker]] |
| F6 | [[features/f6-application-tracking/index\|Application tracking]] | M5 | 9 | [[features/f6-application-tracking/tasks/tracker\|tracker]] |
| F7 | [[features/f7-preference-learning/index\|Preference learning]] | M5 | 9 | [[features/f7-preference-learning/tasks/tracker\|tracker]] |
| F8 | [[features/f8-company-research-agent/index\|Company research agent]] | M5 | 9 | [[features/f8-company-research-agent/tasks/tracker\|tracker]] |
| F9 | [[features/f9-search-and-api/index\|Search & public API]] | M5 | 10 | [[features/f9-search-and-api/tasks/tracker\|tracker]] |
| F10 | [[features/f10-telegram-commands/index\|Telegram command interface]] | M5 | 10 | [[features/f10-telegram-commands/tasks/tracker\|tracker]] |

**120 tasks across 11 features.** Build order and dependencies: [[IMPLEMENTATION-READINESS]] §3.

## Feature-level ADRs

| # | Title | Feature |
|---|---|---|
| [[features/f1-ats-job-discovery/adr/0001-company-registry-seeding\|F1-0001]] | Curated seed plus directory expansion | F1 |
| [[features/f1-ats-job-discovery/adr/0002-immutable-raw-postings\|F1-0002]] | Immutable raw postings with content-hash dedup | F1 |
| [[features/f2-normalization-dedup/adr/0001-conservative-fingerprint\|F2-0001]] | Conservative fingerprint; group, never merge | F2 |
| [[features/f3-claude-batch-enrichment/adr/0001-run-as-resumable-state-machine\|F3-0001]] | The Run as a durable resumable aggregate | F3 |
| [[features/f3-claude-batch-enrichment/adr/0002-pre-submission-cost-ceiling\|F3-0002]] | Estimate and ledger cost before submitting | F3 |
| [[features/f4-cv-matching-ranking/adr/0001-explainable-linear-scoring\|F4-0001]] | Transparent linear scoring, not a learned ranker | F4 |
| [[features/f4-cv-matching-ranking/adr/0002-cv-versioning-and-restaling\|F4-0002]] | Immutable CV versions; re-stale rather than rewrite | F4 |
| [[features/f5-daily-digest-telegram/adr/0001-never-delay-the-digest\|F5-0001]] | 07:00 is a hard commitment; ship partial rather than late | F5 |
| [[features/f5-daily-digest-telegram/adr/0002-delivery-idempotence\|F5-0002]] | Per-card delivery log as the idempotence mechanism | F5 |
| [[features/f6-application-tracking/adr/0001-permissive-transitions-with-history\|F6-0001]] | Permissive transitions, complete history | F6 |
| [[features/f7-preference-learning/adr/0001-transparent-frequency-weighting\|F7-0001]] | Transparent frequency weighting, not a learned ranker | F7 |
| [[features/f7-preference-learning/adr/0002-evidence-threshold-and-explainability\|F7-0002]] | No weight without cited evidence | F7 |
| [[features/f8-company-research-agent/adr/0001-fetch-then-synthesise\|F8-0001]] | Curated fetchers plus synthesis, never open web search | F8 |
| [[features/f4-cv-matching-ranking/adr/0003-pre-match-filter-and-cv-caching\|F4-0003]] | Pre-match filter and CV prompt caching | F4 |
| [[features/f9-search-and-api/adr/0001-index-as-rebuildable-projection\|F9-0001]] | The index is a rebuildable projection | F9 |
| [[features/f10-telegram-commands/adr/0001-declarative-command-registry\|F10-0001]] | Declarative command registry | F10 |
| [[features/f10-telegram-commands/adr/0002-no-conversational-fallback\|F10-0002]] | No LLM in the command path | F10 |

## Interface contracts

| Contract | Feature |
|---|---|
| [[features/f1-ats-job-discovery/contracts/ats-endpoints\|ATS endpoints]] | F1 — the five providers' real shapes |
| [[features/f3-claude-batch-enrichment/contracts/enrichment-schema\|Enrichment schema]] | F3 — schema, prompt, parsing, cost model |
| [[features/f4-cv-matching-ranking/contracts/match-schema\|Match schema]] | F4 — schema, prompt, CV handling, ranking formula |
| [[features/f5-daily-digest-telegram/contracts/telegram-messages\|Telegram messages]] | F5 — layout, callbacks, escaping, commands |
| [[features/f6-application-tracking/contracts/application-api\|Application API]] | F6 — endpoints and the transition matrix |
| [[features/f8-company-research-agent/contracts/research-schema\|Research schema]] | F8 — schema, prompt, citation rules, fetchers |
| [[features/f9-search-and-api/contracts/openapi\|API contract]] | F9 — every endpoint, scope and shape |
| [[features/f10-telegram-commands/contracts/command-catalogue\|Command catalogue]] | F10 — all twenty commands, arguments, output shapes |

---

## Conventions

- **Wikilinks** in the `[[target|alias]]` form throughout, path-qualified where basenames collide.
- **Frontmatter** on every document: `status`, `owner`, `reviewers`, `updated_at`, `stage`, `tags`.
- **Mermaid** for every diagram — C4 for structure, sequence for flows, ER for data, state for machines.
- **English only**, in documents and in code.
- **Acceptance criteria contain no implementation tokens** — no HTTP verbs, paths, status codes, class
  names, JSON or SQL. If an AC names a technology, it is not an acceptance criterion.
