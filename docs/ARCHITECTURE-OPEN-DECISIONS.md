---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "04-05"
ticket: ""
tags: [architecture, open-decisions, jobhunter]
---

# ARCHITECTURE — OPEN DECISIONS

> Decisions that are **not yet made** and are not blocking today, ranked by blast radius.
> When one is decided it becomes an ADR and is struck through here.
>
> Blast radius: 🔴 rewrite · 🟠 multi-feature refactor · 🟡 single-feature change · 🟢 config · 🔵 unknown

| # | Decision | Radius | Blocks | Decide by | Current default |
|---|---|---|---|---|---|
| O1 | Company registry seeding: curated YAML vs ATS-directory crawl vs both | 🟠 | F1 T3 | M2 start | Curated YAML of ~300 companies **plus** a weekly directory crawl for expansion |
| O2 | Is the API internet-facing behind Keycloak, or cluster-internal only | 🟡 | F9 T8 | M5 start | Internet-facing — a reviewer clicking a live URL is worth the marginal risk |
| O3 | RawPosting retention window before pruning | 🟢 | F2 T9 | M2 end | 90 days raw; normalised Jobs kept indefinitely |
| O4 | Does F8 Company Research use Claude web search or curated fetchers | 🟠 | F8 T2 | M5 start | Curated fetchers + Claude synthesis, so every claim carries a URL ([[CONTEXT]] invariant 5) |
| O5 | Salary floor as a hard pre-filter vs a ranking down-weight | 🟡 | F7 T5 | after 200 Signals | Down-weight only; hard filter requires an explicit Owner opt-in |
| O6 | Near-duplicate grouping strategy beyond exact Fingerprint (trigram? embedding?) | 🟠 | F2 T6 | M2 mid | Exact Fingerprint only; group near-duplicates by `(company, title-trigram ≥ 0.85)` for display, never merge |
| O7 | Whether `jobhunter-worker` splits into per-stage deployments | 🟠 | — | when a stage saturates | Single worker, one replica (SAD §11 D2) |
| O8 | Digest card count: fixed top-10 vs score-threshold-driven | 🟢 | F5 T6 | M4 | Score ≥ 70, capped at 10; report the count above threshold |
| O9 | Do we store full JD text indefinitely, or hash + excerpt after N days | 🟡 | F2 T9 | M3 | Full text kept; revisit if the table exceeds 5 GB |
| O10 | CV versioning: re-match window when the CV changes | 🟡 | F4 T7 | M3 | Re-match the last 30 days of live Jobs at cheap tier |
| O11 | Multi-CV / multi-persona targeting (backend vs platform vs AI roles) | 🔴 | — | post-M5 | Out of scope. Would change `Match` from `(job, profile)` to `(job, persona)` |
| O12 | Whether Typesense also serves the Telegram bot's `/search` command | 🟢 | F9 T9 | M5 | Yes, same query service, different renderer |

---

## Decided and closed

| # | Decision | Became |
|---|---|---|
| ~~O0a~~ | ~~Process topology: microservices vs monolith~~ | [[00-overview/adr/0001-modular-monolith-three-deployables\|ADR-0001]] |
| ~~O0b~~ | ~~Transport: Kafka vs RabbitMQ~~ | [[00-overview/adr/0002-rabbitmq-wolverine-transport\|ADR-0002]] |
| ~~O0c~~ | ~~ORM: EF Core vs Dapper~~ | [[00-overview/adr/0003-postgresql-efcore-dapper\|ADR-0003]] |
| ~~O0d~~ | ~~Scheduler: Quartz.NET vs Hangfire~~ | [[00-overview/adr/0004-hangfire-scheduling\|ADR-0004]] |
| ~~O0e~~ | ~~Search: Postgres FTS vs Typesense~~ | [[00-overview/adr/0008-typesense-over-postgres-fts\|ADR-0008]] |

---

## Related

- [[00-overview/sad]] §11 · [[DECISION-LOG]] · [[IMPLEMENTATION-READINESS]]
