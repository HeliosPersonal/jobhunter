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

Only **O2** and **O5** are genuinely open and blocking; every other decision below has either been
resolved by an accepted ADR or settled as fact — see "Decided and closed". `BACKLOG.md` §6 lists only
O2 and O5 as needing an answer.

| # | Decision | Radius | Blocks | Decide by | Current default |
|---|---|---|---|---|---|
| O2 | Is the API internet-facing behind Keycloak, or cluster-internal only | 🟡 | **F9 T04** | M5 start | Internet-facing — a reviewer clicking a live URL is worth the marginal risk |
| O5 | Salary floor as a hard pre-filter vs a ranking down-weight | 🟡 | **F7 T07** | after 200 Signals | Down-weight only; hard filter requires an explicit Owner opt-in |
| O7 | Whether `jobhunter-worker` splits into per-stage deployments | 🟠 | — | when a stage saturates | Single worker, one replica (SAD §11 D2) |
| O9 | Do we store full JD text indefinitely, or hash + excerpt after N days | 🟡 | F2 T9 | M3 | Full text kept; revisit if the table exceeds 5 GB |
| O11 | Multi-CV / multi-persona targeting (backend vs platform vs AI roles) | 🔴 | — | post-M5 | Out of scope. Would change `Match` from `(job, profile)` to `(job, persona)` |

There is no `O13`; the register only ever defined `O1`–`O12`. Any citation of `O13` (e.g. in
ADR-0005) is a typo for O5, the tier/fallback decision.

---

## Decided and closed

| # | Decision | Became |
|---|---|---|
| ✅ O1 | Company registry seeding: curated YAML vs ATS-directory crawl vs both | Resolved by [[features/f1-ats-job-discovery/adr/0001-company-registry-seeding\|ADR-F1-0001]] |
| ✅ O3 | RawPosting retention window before pruning | Settled fact — 90-day retention stated in the F1 and global data models |
| ✅ O4 | Does F8 Company Research use Claude web search or curated fetchers | Resolved by [[features/f8-company-research-agent/adr/0001-fetch-then-synthesise\|ADR-F8-0001]] |
| ✅ O6 | Near-duplicate grouping strategy beyond exact Fingerprint | Resolved by [[features/f2-normalization-dedup/adr/0001-conservative-fingerprint\|ADR-F2-0001]] (computed at digest assembly) |
| ✅ O8 | Digest card count: fixed top-10 vs score-threshold-driven | Fixed in F5 SAD/T03 — score ≥ 70, capped at 10 |
| ✅ O10 | CV versioning: re-match window when the CV changes | Resolved by [[features/f4-cv-matching-ranking/adr/0002-cv-versioning-and-restaling\|ADR-F4-0002]] |
| ✅ O12 | Whether Typesense also serves the Telegram bot's `/search` command | Fixed in F9 T09 — same query service, different renderer |
| ~~O0a~~ | ~~Process topology: microservices vs monolith~~ | [[00-overview/adr/0001-modular-monolith-three-deployables\|ADR-0001]] |
| ~~O0b~~ | ~~Transport: Kafka vs RabbitMQ~~ | [[00-overview/adr/0002-rabbitmq-wolverine-transport\|ADR-0002]] |
| ~~O0c~~ | ~~ORM: EF Core vs Dapper~~ | [[00-overview/adr/0003-postgresql-efcore-dapper\|ADR-0003]] |
| ~~O0d~~ | ~~Scheduler: Quartz.NET vs Hangfire~~ | [[00-overview/adr/0004-hangfire-scheduling\|ADR-0004]] |
| ~~O0e~~ | ~~Search: Postgres FTS vs Typesense~~ | [[00-overview/adr/0008-typesense-over-postgres-fts\|ADR-0008]] |

---

## Related

- [[00-overview/sad]] §11 · [[DECISION-LOG]] · [[IMPLEMENTATION-READINESS]]
