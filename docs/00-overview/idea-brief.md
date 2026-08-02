---
status: Confirmed
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "XL"
stage: "01"
ticket: ""
value_score:
  rice: 100
  state: confirmed
  confirmed_at: "2026-08-02"
feasibility_state: confirmed
tags: [sdlc/stage-01, idea-brief, jobhunter, mvp]
---

# Idea Brief — JobHunter, an AI Job Intelligence Platform

> The brainstorm. This document expands [`AI_Job_Intelligence_Platform_Plan.md`](../../AI_Job_Intelligence_Platform_Plan.md)
> into a decided position: which approach, why, what was rejected, and what is still open.
> Vocabulary is fixed by [[CONTEXT]].

---

## 1. Raw idea

Automatically discover high-quality engineering jobs from ATS feeds, analyse and rank them with
the Claude Batch API against my CV, and deliver one Telegram digest every morning at 07:00.
Track the whole hiring pipeline. Learn my preferences from what I ignore. Research interesting
companies automatically. Build the whole thing as a genuinely event-driven .NET 10 system so it
doubles as the portfolio piece that gets me the AI-first backend roles it is finding.

---

## 2. Problem

Job hunting at senior/staff level is a **high-noise, low-throughput search problem** and the
existing tools optimise for the wrong side of it.

- **Aggregators optimise for volume, I need precision.** LinkedIn's "recommended" feed rewards
  recruiter spend, not fit. 90% of what surfaces is wrong on salary, seniority, contract model or
  timezone — four facts that are decidable in one pass by a model that has read my CV.
- **The good jobs are not on the aggregators first.** They appear on the company's Greenhouse or
  Ashby board hours-to-days before they are syndicated, if they are syndicated at all. By the time
  a role reaches a board the applicant pool is already deep.
- **Reading a job description properly costs 3–5 minutes.** 120 new postings a day is 6–10 hours
  of reading. Nobody does it, so everyone skims titles and misses good roles.
- **The context is never reused.** Every application restarts from zero: what did I learn about
  this company, did I already ignore a near-identical posting, is this the third time this
  recruiter has reposted the same role.
- **Preferences are implicit and never captured.** I know within two seconds that a role is wrong.
  That judgement is thrown away every single time instead of becoming a filter.

The gap: **nothing turns "the entire public ATS surface" into "nine jobs worth reading, ranked,
with reasons" once a day.**

---

## 3. Users

**Primary — the Owner (me).** One person. Senior/staff .NET backend engineer, EMEA timezone,
open to contractor and full-time, salary floor exists and is firm, cares about distributed
systems and AI-adjacent work. Interacts through Telegram at 07:00 and occasionally through a web
API for backfills and analytics.

**Secondary — the reviewer.** A hiring manager or staff engineer who opens the repository because
it is on my CV. They will read `README.md`, this brief, the SAD, one ADR and one feature's task
tracker. They are a *real* user with real acceptance criteria: within ten minutes they should be
able to say "this person can design and ship a distributed system." That constraint is why the
docs are a deliverable, not overhead — see [[../IMPLEMENTATION-READINESS|readiness gates]].

**Explicit non-user:** anyone else. No multi-tenancy ([[CONTEXT]] invariant 9).

---

## 4. Why now

- **Batch inference changed the unit economics.** Anthropic's Message Batches API is 50% cheaper
  and asynchronous by design. Analysing 150 jobs/day with a deep model went from "a toy that costs
  more than the job boards themselves" to about **$31/month** — roughly a dollar a day. The whole
  product is only viable because the expensive stage is batched, and batching happens to be exactly
  the right shape for an event-driven pipeline. Full arithmetic:
  [[../operations/infrastructure]] §8.
- **ATS APIs are stable, public and JSON.** Greenhouse, Lever, Ashby and Workable all expose
  unauthenticated board endpoints. No scraping, no anti-bot arms race, no ToS grey zone for the
  primary path.
- **I already own the runway.** The `helios` k3s cluster ([[../operations/infrastructure|infrastructure]])
  already runs shared PostgreSQL, RabbitMQ, Redis, Typesense, Keycloak and an OTLP pipeline into
  Grafana Cloud. Marginal infra cost for this project is ~zero and the deployment story is proven
  by two sibling projects.
- **.NET 10 is out** and the interesting parts of the showcase — Aspire orchestration, Minimal
  APIs, `BackgroundService`, source-generated JSON, the new Npgsql — are all current.
- **The market I am searching is the market I am demonstrating competence in.** Building an
  AI-integration-heavy distributed system to find AI-integration-heavy distributed-systems jobs is
  a self-reinforcing artifact. That is a real strategic reason, not a cute one.

---

## 5. Out of scope

Full list and rationale in [[CONTEXT]] §4. The headline exclusions:

- LinkedIn scraping, and aggregator boards generally.
- Auto-apply, auto-outreach, or any write action toward an employer.
- Multi-user SaaS, billing, sign-up.
- A web/mobile UI beyond the read-only API. Telegram is the client.
- CV rewriting and cover-letter generation (post-MVP, [[../BACKLOG]]).

---

## 6. Competitive analysis

| # | Product · URL | What it does | Value (1–5) | Gap it leaves |
|---|---|---|---|---|
| 1 | **LinkedIn Jobs** · linkedin.com/jobs | Largest inventory, weak relevance, recruiter-monetised ranking | 2 | No salary truth, no contractor signal, no timezone reasoning, ranking is not mine |
| 2 | **Otta / Welcome to the Jungle** · otta.com | Curated, decent matching, good UX | 3 | Curation caps inventory; no CV-level reasoning; no personal preference learning; opaque scoring |
| 3 | **Hiring Cafe** · hiring.cafe | Aggregates ATS boards directly, strong filters | 4 | Filters, not reasoning — cannot answer "is this contractor-friendly for an EMEA staff engineer" |
| 4 | **JobRight / Simplify / LazyApply** | LLM matching + autofill/auto-apply | 3 | Auto-apply is spray-and-pray; matching quality unverifiable; my data goes to a third party |
| 5 | **Greenhouse/Lever job-board RSS + Zapier** | DIY plumbing | 2 | No enrichment, no ranking, no dedup, no learning — just a firehose in a different pipe |
| 6 | **Manual: 15 bookmarked ATS boards** | Status quo | 1 | Costs an hour a day, degrades to zero within a week |

**Where the wedge is:** #3 proves ATS-direct aggregation works and is legal-clean. #4 proves LLM
matching sells. Nobody combines **ATS-direct inventory + deep per-job LLM reasoning against a real
CV + learned personal preferences + a single low-friction daily surface**, and nobody could sell
it profitably at the per-user LLM cost — but for exactly one user, batched, it costs pocket money.

---

## 7. Strategic approaches

### Approach A — "Firehose + filters" (no LLM)
- **Thesis:** ATS ingestion plus good structured filters (salary regex, title heuristics, location parsing) gets 80% of the value at 5% of the complexity.
- **For whom:** someone who wants results this weekend.
- **Outcome metric:** jobs surfaced per day that pass filters.
- **Key trade-off:** filters cannot read prose. "Contractor-friendly", "AI-first engineering culture", "actually remote in EMEA" live in paragraphs, not fields. Precision plateaus low and never improves.
- **Effort signal:** S (1–2 weeks).
- **Recommended?** No — it fails the primary quality goal (precision) and demonstrates nothing.

### Approach B — "Synchronous LLM per job"
- **Thesis:** call the model per posting as it arrives; simplest mental model, freshest results.
- **For whom:** someone who needs sub-hour latency on new postings.
- **Outcome metric:** time from posting to notification.
- **Key trade-off:** 2× the token price, N× the request overhead, rate-limit fragility, and a
  retry/idempotency problem on every single call. Also produces a notification stream, which is
  the interruption pattern this project exists to eliminate.
- **Effort signal:** M.
- **Recommended?** No — worse economics, worse UX, and the "distributed system" it demonstrates is just a queue with an HTTP client.

### Approach C — "Batched analytical pipeline" ✅
- **Thesis:** ingest continuously and cheaply; concentrate all model work into two daily Batch
  submissions (Enrichment, then Matching); rank locally; deliver one digest. Each pipeline stage is
  an independently-scalable message consumer with its own retry semantics and its own metrics.
- **For whom:** the Owner, who wants one high-signal read per morning, and the reviewer, who wants
  to see event-driven architecture done properly.
- **Outcome metric:** precision@10 of the morning digest; cost per Run; Run success rate.
- **Key trade-off:** up to 24 h latency on a new posting, and the Batch API's asynchronous
  lifecycle (submit → poll → retrieve) forces genuine durable state. Both are acceptable: the
  latency is invisible at daily cadence, and the durable state *is* the interesting engineering.
- **Effort signal:** L (10–12 weeks solo, part-time), phased so F0–F5 is a usable product by ~week 7.
- **Recommended? Yes.**

### Approach D — "Local LLM only (Ollama on helios)"
- **Thesis:** zero marginal cost, full data control, model already running on the cluster.
- **Key trade-off:** an 8–14 B local model materially underperforms on nuanced fit reasoning and
  long job descriptions, and the whole product is only as good as that judgement. Cost saved is
  ~$31/month against electricity and a GPU already paid for; quality lost is the entire value
  proposition. A real trade, but not a close one.
- **Recommended?** Not as the primary path. **Retained as the cheap-tier fallback** for triage and
  as the degraded mode when the Anthropic budget ceiling is hit — see [[adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]].

---

## 8. Multi-perspective feedback

### Engineer
Approach C is the only one where the architecture is load-bearing rather than decorative. The
Batch API's submit/poll/retrieve lifecycle *requires* durable Run state, which *requires* an
outbox and idempotent consumers, which is exactly the distributed-systems substance a reviewer
wants to see. The risk is over-decomposition: nine microservices for one user would be architecture
cosplay and would slow me down. Resolution: **three deployables (Api, Worker, Telegram), nine
logical stages, RabbitMQ between them** — real message boundaries, honest process count. See
[[adr/0001-modular-monolith-three-deployables|ADR-0001]].

Second concern: LLM output parsing is where these projects rot. Mitigation is a hard contract —
tool-use structured output with a JSON Schema, tolerant parsing that degrades to a safe default
rather than throwing, and unit tests against saved fixtures with zero network
([[adr/0006-structured-output-contract|ADR-0006]]).

### Executive
The product must be judged on one number: **how many mornings out of ten do I find something worth
opening.** Everything else is engineering self-indulgence. That implies precision@10 is the KPI
from day one, that the digest must be brutally short, and that the preference-learning feature
(F7) is not a nice-to-have — without it, precision degrades as I ignore the same 40 job archetypes
week after week and nothing changes.

Cost must be bounded and visible or the project becomes an anxiety source. A hard per-Run ceiling
enforced *before* submission, not a budget alert after the fact.

Also: the showcase value has a shelf life. Ship F0–F5 in seven weeks or the portfolio argument
evaporates.

### UX-researcher
The interaction surface is a Telegram message read half-awake with one thumb. That is a severe
constraint and it should drive the design, not be an afterthought:
- The first screen must answer "is today worth my attention" in under three seconds — counts, best
  match, average salary. Everything else is below the fold.
- Cards must be scannable: title, company, score, three reasons, four buttons. No paragraphs.
- The four actions (Open / Ignore / Save / Applied) must be one tap and instantly acknowledged;
  a tap that appears to do nothing kills trust in the whole system.
- **Ignoring must feel productive.** If the Owner learns that ignoring teaches the system, ignore
  rate becomes engagement rather than churn. The digest should say "I stopped showing you 34 jobs
  below your salary floor" — that sentence is the single strongest retention mechanism available.

### Synthesis matrix

| Concern | Engineer | Executive | UX | Resolution |
|---|---|---|---|---|
| Decomposition | Real boundaries, few processes | Don't slow delivery | — | ADR-0001: 3 deployables, 9 stages, RabbitMQ |
| LLM reliability | Schema + tolerant parse + fixtures | Cost ceiling | Never show an unexplained score | ADR-0006 + invariants 4 & 6 |
| Latency | 24 h is fine | 24 h is fine | Daily rhythm is the *feature* | Accepted; no realtime path in MVP |
| Preference learning | Nontrivial, needs Signals from day one | KPI-critical, not optional | Makes ignoring feel productive | F7 is MVP-scope; Signal capture ships with F5 |
| Local model | Good fallback | Free | Invisible | Cheap tier + budget-exceeded degraded mode |

---

## 9. Trade-offs and edge cases

| Approach | Pros | Cons |
|---|---|---|
| A — filters only | Fast, cheap, simple | Low precision ceiling; no learning; no showcase value |
| B — sync LLM | Fresh; simple mental model | 2× cost; fragile; interruption-based UX |
| **C — batched pipeline** | Best precision/cost; resumable; showcase-grade; learning-ready | 24 h latency; durable-state complexity; larger build |
| D — local only | Free; private | Materially worse judgement; kills the value prop |

### Edge cases that shape the design

- **The same job posted on three boards** (company site + Greenhouse + a mirrored feed) → Fingerprint dedup on `(domain, normalised title, location set)`; merge into earliest-seen, keep aliases.
- **A recruiter reposts the same role every 14 days** to refresh it → dedup must survive across Runs and across `posted_at` changes; treat as the same Job with an updated `last_seen_at`.
- **A job is pulled mid-pipeline** (404 between Discovery and Delivery) → Delivery must re-verify liveness of the top-N apply URLs before rendering; dead jobs are dropped with a note, not shown.
- **Claude returns malformed JSON for 3 of 150 items in a batch** → per-item tolerant parse; failed items are recorded as `EnrichmentFailed` and retried once in the next Run at cheap tier; the Run does not fail.
- **A Batch is still `in_progress` at 07:00** → deliver the digest with the items that completed and an explicit "N jobs still processing, they'll be in tomorrow's digest" line. Never delay the 07:00 slot.
- **The cost ceiling is hit mid-Run** → abort the Stage, mark `CostAborted`, deliver a reduced digest with a visible warning. Never silently truncate (invariant 6).
- **Zero new jobs today** → still deliver a digest. Silence is indistinguishable from breakage.
- **An ATS returns 429 for a whole day** → per-host token bucket + quarantine after two consecutive failures; the digest reports degraded sources.
- **The CV changes** → all Matches computed against the previous CV version are stale; re-match the last 30 days of live Jobs on the next Run, at cheap tier.
- **A company changes ATS** (Lever → Ashby) → binding detection re-runs weekly; old binding is retired, jobs are not orphaned because the key is the Company domain, not the board token.
- **Timezone/DST at 07:00 Europe/Kyiv** → schedule in the zone, not in UTC offsets.

---

## 10. Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | LLM scoring is subjectively wrong — the digest surfaces jobs I don't want | Medium | Critical | Precision@10 tracked from day one; reasons always shown; F7 preference learning closes the loop; prompt fixtures + golden-set regression tests |
| R2 | ATS endpoints change shape or close | Medium | High | One adapter per ATS behind `IJobSource`; contract tests against recorded fixtures; a broken adapter degrades one source, not the Run |
| R3 | Cost overrun from a runaway Run | Low | High | Hard pre-submission ceiling; per-Run cost ledger; alert at 70% |
| R4 | Batch API latency/failure at the wrong hour | Medium | Medium | Partial-digest policy; Batch state is durable and resumable; poll with backoff |
| R5 | Dedup is too aggressive and hides a real job | Medium | Medium | Fingerprint is conservative (exact normalised triple); near-duplicates are grouped, not dropped; alias table is auditable |
| R6 | Scope creep — nine features never ship | High | High | Phased milestones M1–M5; F0–F5 is a shippable product; F6–F9 are additive |
| R7 | Solo-project rot: no tests, docs drift | Medium | High | >90% coverage gate in CI; docs-first SDLC with readiness gates; every feature has a test plan before tasks |
| R8 | Personal data (CV) handling | Low | Medium | CV text stays in own Postgres; sent to Anthropic only as prompt content; no third-party job-tool gets it; secrets via Infisical |
| R9 | helios cluster is a single home node | Medium | Low | Accepted. Data is reproducible from source; Postgres backup to Azure Blob nightly; downtime costs one digest |

---

## 11. RICE

- **Reach (R)**: 100 — every morning, the single user, every workday.
- **Impact (I)**: 3 — replaces a 1 h/day manual process with a 5 min read, *and* delivers the portfolio artifact. Dual payoff.
- **Effort (E)**: 3 person-months part-time (F0–F9); 1.75 to the first shippable digest (F0–F5).
- **Confidence (C)**: 1.0 — every external dependency (ATS JSON, Batch API, Telegram, helios) is proven and already used in a sibling project.
- **RICE = R × I × C / E = 100 × 3 × 1.0 / 3 = 100**
- **State**: confirmed

---

## 12. Feasibility

- [x] **Tech** — .NET 10 SDK present (`10.0.302`); helios provides Postgres, RabbitMQ, Redis, Typesense, Keycloak, OTLP; Anthropic Batch API access confirmed; Telegram bot token obtainable in minutes.
- [x] **Skills** — .NET, EF Core, messaging, k8s, Terraform all exercised in `wisewizard` and `overflow`. The one genuinely new piece is the Batch lifecycle, already prototyped in `wisewizard`'s `ILlmClient`.
- [x] **Time** — 10–12 weeks part-time to full scope; 7 weeks to a working morning digest.
- **State**: confirmed

---

## 13. Recommendation

**Build Approach C.** A batched, event-driven analytical pipeline on .NET 10, three deployables,
RabbitMQ between nine logical stages, PostgreSQL as the single source of truth, Anthropic Message
Batches for the two expensive stages, Telegram as the only client.

Sequence the work so the product is useful early and the showcase compounds:

| Milestone | Weeks | Contents | Definition of "done" |
|---|---|---|---|
| **M1 — Skeleton** | 1–2 | F0 platform foundation | `dotnet run` via Aspire, one green integration test, CI deploys a hello-world pod to `apps-staging` |
| **M2 — Inventory** | 3–4 | F1 discovery, F2 normalization/dedup | 5 000 live Jobs in Postgres from ≥4 ATS kinds, dedup rate reported |
| **M3 — Intelligence** | 5–6 | F3 enrichment, F4 matching/ranking | Every new Job carries an Enrichment and a Match; cost per Run < $0.50 |
| **M4 — The product** | 7 | F5 digest + Telegram | A real digest lands at 07:00 with working action buttons. **First shippable release.** |
| **M5 — Compounding** | 8–12 | F6 tracking, F7 learning, F8 research, F9 search/API | Precision@10 measurably improves over M4 baseline |

---

## 14. Parked and rejected approaches

| # | Approach | Status | Reason | Revisit trigger |
|---|---|---|---|---|
| 1 | LinkedIn scraping | ❌ Rejected | Anti-bot arms race, ToS exposure, brittle | An official jobs API with sane terms |
| 2 | Auto-apply | ❌ Rejected | Invariant 7; spray-and-pray damages reputation | Never |
| 3 | Synchronous per-job LLM calls | ❌ Rejected | 2× cost, interruption UX, no durable-state showcase | Sub-hour latency becomes a real requirement |
| 4 | Nine microservices, one per stage | ❌ Rejected | Operational cost with no benefit at one user; ADR-0001 | Multi-tenant, or a stage needs independent scaling |
| 5 | Local Ollama as primary model | ⚖️ Parked | Quality gap on nuanced fit reasoning | Local models close the gap, or Anthropic pricing changes |
| 6 | Postgres full-text search instead of Typesense | ⚖️ Parked | Typesense is already running on helios and gives typo tolerance + faceting free; ADR-0008 | Typesense becomes an operational burden |
| 7 | Vector embeddings + semantic retrieval for matching | ⚖️ Parked | Adds a store and a tuning surface; the Batch prompt already sees the full CV and full JD, so retrieval solves a problem we don't have at 150 jobs/day | Job volume exceeds ~2 000/day, or multi-CV targeting arrives |
| 8 | Kafka instead of RabbitMQ | ❌ Rejected | RabbitMQ is already on the cluster; no replay/log-compaction requirement at this volume | Event replay or stream processing becomes a requirement |
| 9 | Email + Slack delivery channels | ⚖️ Parked | Telegram covers the need; multi-channel is an abstraction cost with no user | Telegram becomes unavailable, or a second reader appears |
| 10 | CV rewriting / cover letters | ⚖️ Parked | Different product; would dominate the roadmap | After M5 |

---

## 15. Open questions

- [ ] Which companies seed the registry, and how is the seed list maintained? — owner: Viacheslav — *leaning: a curated YAML of ~300 target companies in the repo, plus weekly ATS-directory crawl for expansion. Decided in F1.*
- [ ] Exact salary-floor value and whether it hard-filters or only down-weights before F7 has data. — owner: Viacheslav — *leaning: down-weight only until 200 Signals exist.*
- [ ] Is the read API public-on-the-internet (behind Keycloak) or cluster-internal only? — owner: Viacheslav — *leaning: internet-facing behind Keycloak, because a reviewer clicking a live URL is worth more than the marginal risk. Decided in F9.*
- [ ] Retention: how long are RawPostings kept before pruning? — owner: Viacheslav — *leaning: 90 days raw, forever for normalised Jobs.*
- [ ] Does the Company Research agent use Claude with web search, or a curated set of fetchers? — owner: Viacheslav — *decided in F8; leaning: curated fetchers + Claude synthesis, so every claim has a URL (invariant 5).*

---

## Related

- [[CONTEXT]] — the vocabulary and invariants this brief assumes
- [[sad|System Architecture Document]] — the design that implements Approach C
- [[../DECISION-LOG]] — cross-cutting decisions
- [[../BACKLOG]] — milestone plan and post-MVP items
- [[../features/f0-platform-foundation/index|F0]] … [[../features/f9-search-and-api/index|F9]] — the features

## DoD self-check

- [x] Every section 1–15 present and filled
- [x] ≥3 strategic approaches with an explicit recommendation
- [x] Competitive analysis has ≥5 entries with a stated gap each
- [x] Multi-perspective feedback from ≥3 distinct roles, with a synthesis matrix
- [x] Risks table has likelihood, impact and a concrete mitigation per row
- [x] RICE and feasibility both `confirmed`
- [x] Rejected approaches recorded with a revisit trigger
- [x] Every open question has an owner and a leaning
