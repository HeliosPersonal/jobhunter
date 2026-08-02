---
status: Final
owner: "Viacheslav Melnichenko"
updated_at: "2026-08-02"
stage: "00"
tags: [audit, sdlc, tracker, parallel, implementation, jobhunter]
---

# Implementation Tracker — Parallel Work Plan

> A **general, parallelism-first** roll-up of the 125 tasks across 11 features. Its purpose is to let
> several engineers/agents work **concurrently without migration or file contention**. It groups tasks
> into **lanes** — a lane is a stretch of work one agent can own end-to-end with no cross-lane write
> conflicts. Read alongside [[AUDIT-RESOLUTION-DECISIONS]] (canonical values) and
> [[IMPLEMENTATION-READINESS]] §3 (the dependency graph this plan is derived from).
>
> **One task, one PR, ≤500 LOC, ≤1 day** still holds. This document adds the *cross-task* dimension:
> which PRs can be open at the same time.

---

## 1. How to read this

- **Wave** — a horizontal cut of the build-order DAG. Everything in a wave can proceed once the prior
  wave's *interfaces* (not necessarily every task) are merged. Waves are the coarse gate.
- **Lane** — a vertical strip of work owned by one agent within a wave. Lanes in the same wave touch
  **disjoint files** by construction, so they never merge-conflict. The number of lanes = the max
  useful parallelism for that wave.
- **Contention key** — the shared resource a lane writes. Two lanes may run in parallel **iff their
  contention keys differ**. The migration file namespace is partitioned by feature-owned table set
  (see [[AUDIT-RESOLUTION-DECISIONS]] §1 ownership table).
- **Gate** — what must be true before the lane may start (an interface, an ADR, or an open decision).

Status legend: `[ ]` ready · `[?]` blocked by an open decision · `[~]` in progress · `[x]` done.

---

## 2. Critical path (longest dependency chain)

```
F0 (platform) ─► F1 (discovery) ─► F2 (normalize) ─► F3 (enrich) ─► F4 (match) ─► F5 (digest, SHIP)
```

Six features, strictly sequential at the *feature-interface* level. Everything else hangs off this
spine and parallelises. **The schedule is bounded by this chain**, so staffing beyond it buys
wall-clock only where a wave has multiple lanes. Target the critical path first; fill idle agents with
off-spine lanes.

---

## 3. Waves and lanes

### Wave 0 — Platform foundation (F0) · gate: none · **max 3 parallel lanes**

F0 is the only feature whose failure blocks all others; its internal tasks fan out well because they
touch different subsystems. Contention is low — each lane owns a different infra concern.

| Lane | Tasks | Contention key | Gate |
|---|---|---|---|
| **0A — solution & persistence** | F0 T01 (solution + `JobHunter.TestKit`), T02 (TestKit), migrations bootstrap, T05 DB | `.slnx`, `Infrastructure`, EF context | `[ ]` |
| **0B — bus, scheduler, telemetry** | F0 T04 (RabbitMQ/Wolverine, Ollama container), Hangfire, T11 (8 instruments), observability | messaging + Hangfire schema + OTel | `[ ]` |
| **0C — CI/CD, arch tests, ops** | F0 T12 (8 arch rules), CI, coverage gate, **T15 backup job**, **T16 `replay-dlq` CLI** | `.github/`, `ArchitectureTests`, k8s manifests | `[ ]` |

> Lane 0A publishes the `TestKit` + EF context that 0B/0C and every later wave depend on — land its
> interface first, then 0B/0C run alongside its remaining tasks.

### Wave 1 — Discovery & normalisation (F1 → F2) · gate: F0 interface · **2 lanes, then 1**

F1 and F2 are sequential (F2 reads `raw_postings`), but F1's *adapters* parallelise internally.

| Lane | Tasks | Contention key | Gate |
|---|---|---|---|
| **1A — F1 registry & fetch** | F1 T01–T07 registry/detection/polite-fetch, `raw_postings` (`ON CONFLICT DO UPDATE last_seen_at`), **T13 `JobClosed` publisher** | `companies`, `raw_postings`, `source_fetch_log` | `[ ]` F0 |
| **1B — F1 ATS adapters** | one adapter per ATS provider (fixture-driven, zero network) | `Scrapers/*` (one file per provider — **fully parallel across providers**) | `[ ]` F0 |
| **1C — F2 normalize & dedup** | F2 T01–T09 canonical Job, fingerprint, `job_aliases` (first/last_seen_at), lifecycle, retention prune, authorization AC | `jobs`, `job_aliases` | `[ ]` F1 interface |

> **1B is the widest natural fan-out in the system** — five ATS adapters, each a separate file behind
> `IJobSource`, no shared writes. Give each provider to its own agent.
> F2 T08's near-duplicate grouper has **moved to F5 T13** (ADR-F2-0001) — do not implement it in F2.

### Wave 2 — Enrichment & the LLM spine (F3 → F4) · gate: F2 Jobs · **2 lanes**

| Lane | Tasks | Contention key | Gate |
|---|---|---|---|
| **2A — F3 Run/Batch machinery** | F3 T01–T12 Run aggregate, cost ceiling (pre-submission), enrichment, crash matrix (8 checkpoints), **T14 Ollama fallback** | `runs`, `batches`, `batch_items`, `enrichments` | `[ ]` F2 |
| **2B — F4 match & rank** | F4 T01–T13 Match/Score, CV boundary + sentinel scan, ADR-F4-0003 pre-match filter, calibration (AC-12/13 + reference corpus), cache-control constraint | `matches`, `scores` | `[ ]` F3 Run interface |

> 2A and 2B share the Run/Batch/poller/ledger abstraction but **F4 reuses F3 unchanged** ("no F3 file
> modified" is a DoD). So once F3's Run interface is merged, 2B proceeds without touching 2A's files.
> The `scores`-without-`matches` row (`}o--||`) is the only schema subtlety — owned entirely by F4.

### Wave 3 — Ship: daily digest (F5) · gate: F4 Scores · **1 lane (release-critical)**

| Lane | Tasks | Contention key | Gate |
|---|---|---|---|
| **3A — F5 digest & delivery** | F5 T01–T13 assembly, **T13 near-duplicate grouper** (relocated from F2), delivery idempotence, degraded-day variants, apply-link verification (present-with-flag on timeout / suppress on unreachable), T11 seven bootstrap commands | `digests`, `digest_cards`, `delivery_log` | `[ ]` F4 |

> **M4 = first shippable release.** Keep this lane single-owner and tightly reviewed; it is the
> product's visible surface. `/start`, `/help`, `/digest` ship here as the bootstrap subset.

### Wave 4 — Additive features (F6, F7, F8, F9) · gate: F5 (F6/F7), F4 (F8), F2 (F9) · **up to 4 parallel lanes**

**This is the widest parallel wave.** Four features, four disjoint table-sets, four independent agents.
They may be reordered freely (readiness §3: "F6–F9 are additive").

| Lane | Feature | Tasks | Contention key | Gate |
|---|---|---|---|---|
| **4A** | F6 Application tracking | T01–T09; 7×7=49 transition matrix, `Application` state diagram, invariant-7 negative test; endpoints depend on **F9 T04** | `applications` | `[ ]` F5; endpoints need F9 T04 |
| **4B** | F7 Preference learning | T01–T09; bounded explainable weights, indifferent-profile test; `signals.kind` (8), `suppression_overrides`; endpoints depend on **F9 T04** | `signals`, `suppression_overrides` | `[ ]` F5; **T07 `[?]` O5**; endpoints need F9 T04 |
| **4C** | F8 Company research | T01–T09; fetch-then-synthesise, `research_sources`/`research_claims` FK (invariant 5), SSRF suite; whitelisted writes to `companies.stage`/`employee_band` | `research_sources`, `research_claims` | `[ ]` F4 |
| **4D** | F9 Search & API | T01–T10; Typesense projection, OpenAPI (incl. **CV endpoints T05**, **Run start/abort T06**), `sub`-checked policies | `typesense`, API host, `command`-free | `[ ]` F2; **T04 `[?]` O2** |

> **Sequencing note:** F9 T04 (api-host-auth) is a dependency of F6/F7 endpoints and should be pulled
> **early** within 4D so 4A/4B don't stall. Pull F9 forward if a live demo URL is wanted (readiness §3).
> Only **two tasks in the whole wave are decision-blocked**: F9 T04 (O2) and F7 T07 (O5).

### Wave 5 — Command surface (F10) · gate: each underlying feature · **incremental, not a single lane**

F10 is "last by construction — a surface over everything else." Each command lands as its owning
feature does; F10 T01–T10 build the registry, catalogue (22 commands), `CommandCapability`
sensitivity flag, and the bidirectional conformance test.

| Lane | Tasks | Contention key | Gate |
|---|---|---|---|
| **5A — F10 registry & catalogue** | F10 T01–T10; `command_invocations`, capability flag, `/cv` read-only, `/stats` retained | `command_invocations`, `TelegramCommands/*` | `[ ]` per-command owner merged |

> Command handlers are **owned by their feature** (§8 of the decisions doc): `/pipeline`,`/note`→F6;
> `/search`→F9; `/hidden`→F7; `/saved`,`/stats`→F5; the rest→F10. F10 registers, it does not
> re-implement. So 5A's registry work parallelises with Wave 4 as long as it only defines the registry
> contract; the *handler* rows fill in as each feature's lane merges.

---

## 4. Parallelism summary

| Wave | Features | Max useful agents | Wall-clock note |
|---|---|---|---|
| 0 | F0 | 3 | infra concerns are disjoint |
| 1 | F1, F2 | **5+** (one per ATS adapter in 1B) | widest fan-out; F2 waits on F1 interface |
| 2 | F3, F4 | 2 | spine; F4 reuses F3 unchanged |
| 3 | F5 | 1 | release-critical, single-owner |
| 4 | F6, F7, F8, F9 | **4** | widest feature-level parallelism; 4 disjoint table-sets |
| 5 | F10 | 1 + per-feature handlers | incremental, trails Wave 4 |

**Peak concurrency** is Wave 1 (adapter fan-out) and Wave 4 (four independent features). Staffing
beyond ~5 concurrent agents yields diminishing returns because the F0→F5 spine is sequential.

### Why these lanes never conflict

1. **Migration namespace is partitioned by table ownership** ([[AUDIT-RESOLUTION-DECISIONS]] §1). No
   two lanes write the same table; the three cross-owner writes are an explicit whitelist, coordinated
   by a single reviewed seam each.
2. **Every external dependency sits behind a port** (`IJobSource`, `ILlmBatchClient`, …). Adding a
   provider is a new file, never a change to the pipeline — which is exactly what makes 1B fan out.
3. **Shared abstractions are frozen once merged** — F3's Run/Batch is reused by F4/F5/F8 with "no F3
   file modified" as a DoD, so downstream lanes read it but never edit it.
4. **F10 registers rather than re-implements**, so the command surface doesn't contend with feature
   handlers.

---

## 5. Blocked-task register (the only things not "ready")

| Task | Blocker | Unblocks when |
|---|---|---|
| F9 T04 (internet-facing API) | **O2** open | Owner decides internet-facing vs cluster-internal |
| F7 T07 (weight/evidence floor) | **O5** open | Owner decides salary floor: hard filter vs down-weight |

Everything else is `[ ]` ready. `BACKLOG.md` §6 tracks O2 and O5 as the only decisions needing an
answer.

---

## 6. Suggested first cut (3 agents, matching the goal)

Because the goal is to run **up to 3 agents in parallel**, the natural first assignment mirrors the
disjoint file partitions already proven safe during audit remediation:

- **Agent 1 → the spine head:** Wave 0 lane 0A + Wave 1 lane 1A (platform + discovery core). This is
  the critical path; start it first.
- **Agent 2 → adapters & infra:** Wave 0 lanes 0B/0C + Wave 1 lane 1B (bus/CI/adapters) — no overlap
  with Agent 1's table-set.
- **Agent 3 → off-spine prep:** design/scaffold Wave 4 lanes 4C (F8) and 4D (F9 non-endpoint parts),
  which only need F2/F4 interfaces and touch entirely separate tables.

As the spine advances, re-home agents onto Wave 2 (2A/2B) then converge on Wave 3 (F5) for the M4 ship,
then fan back out across Wave 4.

---

## Related

- [[AUDIT-RESOLUTION-DECISIONS]] — canonical values every lane must honour
- [[SDLC-AUDIT]] §0 — the defect resolution log
- [[IMPLEMENTATION-READINESS]] §3 — the dependency DAG this plan derives from
- [[README]] — feature index and task counts
