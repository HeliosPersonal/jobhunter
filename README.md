# JobHunter

**An AI job-intelligence platform.** It watches the public ATS surface, analyses every new engineering
role with the Claude Message Batches API, ranks the results against a CV, and delivers one Telegram
digest at 07:00 — nine jobs worth reading, ranked, with reasons.

It also learns. Every *Ignore* teaches it something, and the digest tells you what it stopped showing
you and why.

```
🌅 Good morning.

127 new · 9 strong matches · avg 185k USD

🏆 Staff Backend Engineer — Snowflake · 95
   Kafka · Azure · distributed systems

9 cards below. 34 hidden (salary floor, timezone).
```

---

## Why it exists

Job hunting at senior level is a high-noise search problem, and the existing tools optimise for the
wrong side of it. Aggregators reward recruiter spend rather than fit; the good roles appear on a
company's own Greenhouse or Ashby board hours before they are syndicated, if ever; and reading 120
postings properly costs six hours a day, so nobody does it.

Batch inference changed the economics. Analysing 150 jobs a day with a capable model went from
"costs more than the job boards themselves" to about a dollar a day — and batching happens to be
exactly the right shape for an event-driven pipeline.

Full rationale, competitive analysis and the approaches that were rejected:
[docs/00-overview/idea-brief.md](docs/00-overview/idea-brief.md).

---

## How it works

```
Companies → ATS boards → RawPosting → Job → Enrichment → Match → Score → Digest → Telegram
   F1           F1          F1        F2       F3          F4      F4       F5        F5
```

Nine logical stages, each an independent message consumer over RabbitMQ, hosted by three deployables.
The two expensive stages run once a day as Anthropic **batch** submissions, which is what makes the
economics work — and what forces the durable, resumable `Run` state machine that is the most
interesting part of the system.

| | |
|---|---|
| **Runtime** | .NET 10 · ASP.NET Core Minimal API · Worker Service |
| **Data** | PostgreSQL (EF Core writes, Dapper reads) · Redis · Typesense |
| **Messaging** | RabbitMQ + Wolverine, with an EF Core transactional outbox |
| **Scheduling** | Hangfire on PostgreSQL |
| **AI** | Anthropic Message Batches API, two-tier model cascade, hard cost ceiling |
| **Delivery** | Telegram bot with inline actions |
| **Observability** | OpenTelemetry → Grafana Alloy → Grafana Cloud |
| **Deployment** | Kustomize + GHCR + GitHub Actions → k3s |
| **Local dev** | .NET Aspire — one command |

Architecture: [docs/00-overview/sad.md](docs/00-overview/sad.md) ·
Decisions: [15 system ADRs](docs/00-overview/adr/) + 14 feature ADRs

---

## Run it

```bash
git clone git@github.com:<owner>/jobhunter.git && cd jobhunter

dotnet user-secrets --project src/Aspire/JobHunter.AppHost set "Anthropic:ApiKey"     "sk-ant-..."
dotnet user-secrets --project src/Aspire/JobHunter.AppHost set "Telegram:BotToken"    "1234:ABC..."
dotnet user-secrets --project src/Aspire/JobHunter.AppHost set "Telegram:OwnerChatId" "123456789"

dotnet run --project src/Aspire/JobHunter.AppHost
```

Aspire provisions PostgreSQL, RabbitMQ, Redis, Typesense and Ollama as containers and wires everything
together. Without an Anthropic key the pipeline still runs end to end against the local Ollama model —
only enrichment and matching quality degrade.

Details: [docs/engineering/local-development.md](docs/engineering/local-development.md)

---

## Status

**Pre-code.** The design is complete and gated; implementation has not started.

| | |
|---|---|
| Features specified | 10, all past the [readiness gate](docs/IMPLEMENTATION-READINESS.md) |
| Tasks planned | 108, each one reviewable PR |
| ADRs | 15 system-level + 14 feature-level |
| First shippable release | **M4** — a real digest at 07:00, ~7 weeks in |

| Milestone | Contents | Exit criterion |
|---|---|---|
| M1 | [F0 Platform foundation](docs/features/f0-platform-foundation/index.md) | One command brings the system up; CI deploys to staging |
| M2 | [F1 Discovery](docs/features/f1-ats-job-discovery/index.md), [F2 Dedup](docs/features/f2-normalization-dedup/index.md) | 5 000 live jobs from ≥4 ATS kinds |
| M3 | [F3 Enrichment](docs/features/f3-claude-batch-enrichment/index.md), [F4 Matching](docs/features/f4-cv-matching-ranking/index.md) | Every job enriched and matched; a Run costs < $0.50 |
| **M4** | [F5 Digest & Telegram](docs/features/f5-daily-digest-telegram/index.md) | **A real digest lands at 07:00 with working buttons** |
| M5 | [F6](docs/features/f6-application-tracking/index.md) · [F7](docs/features/f7-preference-learning/index.md) · [F8](docs/features/f8-company-research-agent/index.md) · [F9](docs/features/f9-search-and-api/index.md) | `precision@10` measurably above the M4 baseline |

Roadmap: [docs/BACKLOG.md](docs/BACKLOG.md)

---

## Documentation

This project is documented before it is built, and the documentation is part of the deliverable.

**Start here:** [READING-GUIDE.md](READING-GUIDE.md) — how the docs are organised and in what order to
read them. Full index: [docs/README.md](docs/README.md).

**Want to change it rather than read it?** [docs/DECISIONS-MATRIX.uk.md](docs/DECISIONS-MATRIX.uk.md)
(Ukrainian) is the reconfiguration control panel: 47 decisions and 36 tunable parameters, each as a
menu with the chosen option marked, its blast radius, and what it costs to switch.

If you have ten minutes and want the substance:

1. [docs/CONTEXT.md](docs/CONTEXT.md) §3 — the twelve invariants the whole system is built to preserve
2. [ADR-0001](docs/00-overview/adr/0001-modular-monolith-three-deployables.md) — nine stages, three
   processes, and why not nine microservices
3. [F3's SAD §6](docs/features/f3-claude-batch-enrichment/sad.md) — the resumable Run: surviving a
   five-hour asynchronous batch across process restarts without paying twice
4. [F3's tracker](docs/features/f3-claude-batch-enrichment/tasks/tracker.md) — how that becomes
   thirteen day-sized tasks

---

## A few things it deliberately does not do

No LinkedIn scraping. No auto-apply. No multi-tenancy. No web UI.
Reasons for each: [docs/CONTEXT.md §4](docs/CONTEXT.md).

## Cost

About **$31 a month**, essentially all of it Anthropic API usage at 150 newly discovered jobs a day.
Everything else runs on an existing home-lab cluster at zero marginal cost.

Batch inference is what makes that number work — it halves every token, and the two expensive stages
run as one daily batch each. The single largest line is CV matching; a pre-match filter (most jobs are
ruled out on salary, timezone and remote policy without ever needing a CV comparison) plus prompt
caching of the shared CV prefix cut it by roughly two thirds.

Full breakdown, model IDs, and sensitivity to job volume:
[docs/operations/infrastructure.md](docs/operations/infrastructure.md) §8.

---

## License

MIT
