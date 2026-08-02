---
status: Final
owner: "Viacheslav Melnichenko"
reviewers: ["Architecture Review Board"]
updated_at: "2026-08-02"
stage: "00"
ticket: ""
tags: [audit, sdlc, gate, readiness, jobhunter]
---

# SDLC AUDIT — JobHunter

> **Final SDLC gate before implementation.** Independent review of all 260 documents against the
> premise that implementation begins tomorrow with a team of 30 engineers.
>
> **Verdict: REJECTED** — 31 defects, 9 blocking. Remediation plan in §13 and §15.

---

## 1. Executive Summary

**Verdict: REJECTED.** 31 defects, 9 of them blocking.

One framing point first, because it changes how every severity below should be read. This
documentation set is written explicitly and consistently for **one part-time solo engineer** —
SAD §2 C9 ("One part-time solo engineer. Every design choice is also a schedule choice"), every
tracker's "Owner: Viacheslav (solo)", 500-LOC PRs, an 8-week gantt. The audit premise is 30
engineers starting tomorrow. Against that premise the corpus is **structurally sound but not
integration-safe**: the design decisions are unusually well-argued and the invariants are mostly
enforced by constraints and tests rather than prose, but the *connective tissue* — the global data
model, the canonical vocabulary, the open-decision register, the estimate roll-ups — has drifted
from the feature documents that refine it. A solo author who holds the whole design in their head
survives that drift. Thirty engineers writing migrations in parallel do not.

The single worst finding is not any individual contradiction. It is that
**`IMPLEMENTATION-READINESS.md` certifies all eleven features "Ready" while `BACKLOG.md` §6 and
`ARCHITECTURE-OPEN-DECISIONS.md` simultaneously list ten unresolved decisions blocking eleven named
tasks** — four of which have *already been resolved by accepted ADRs* that nobody closed out. The
gate that exists to prevent starting work prematurely is self-certifying and demonstrably wrong.
Everything else in this report is downstream of the fact that no one re-ran the gate after the
feature documents were written.

Second worst: `docs/architecture/data-model.md` declares itself "**the authoritative schema**" and is
contradicted by **eight of ten** feature data models — not only on added columns, but structurally.
`research_claims` is `source_url text NOT NULL` globally and `source_id uuid NOT NULL FK` in F8.
Those are different schemas, and both are labelled normative.

What is genuinely good, and should not be lost in a rewrite: the invariant-to-constraint mapping
(invariant 8 *is* a unique index; invariant 5 *is* a NOT NULL FK), the absence-assertion tests (the
cost ceiling test passes only if the client is never called), the crash matrix, the CV sentinel scan
with no allowlist, and the bidirectional registry↔catalogue conformance test in F10. That is better
than most production systems have.

**Two defects in this report are in work produced in the session immediately preceding the audit**
(D-27, D-28 in §9). They are listed at the same severity as everything else.

---

## 2. Repository Inventory

260 markdown files · 21,971 lines · 120 task files · 32 ADRs (15 system + 17 feature) · 11 trackers ·
1,746 wikilinks, all resolving.

| F | Feature | Purpose | Missing artifacts | Complete | Ready | Risk |
|---|---|---|---|---|---|---|
| F0 | Platform foundation | Solution, DB, bus, scheduler, telemetry, CI/CD | — (contracts n/a) | 100% | 85% | 🟡 |
| F1 | ATS job discovery | Registry, detection, polite fetch, immutable raw | — | 100% | 70% | 🟠 |
| F2 | Normalization & dedup | Canonical Job, fingerprint, lifecycle | contracts (justified) | 95% | 65% | 🟠 |
| F3 | Claude batch enrichment | Run/Batch machinery, cost ceiling, enrichment | — | 100% | 75% | 🟠 |
| F4 | CV matching & ranking | Match, Score, CV boundary, pre-filter | — | 90% | **55%** | 🔴 |
| F5 | Daily digest & Telegram | Assembly, delivery idempotence, callbacks | — | 100% | 80% | 🟡 |
| F6 | Application tracking | Lifecycle, history, reminders, outcomes | — | 90% | **60%** | 🔴 |
| F7 | Preference learning | Signals → bounded explainable weights | contracts (justified) | 95% | 70% | 🟠 |
| F8 | Company research | Fetch-then-synthesise, cited claims | — | 95% | 70% | 🟠 |
| F9 | Search & API | Typesense projection, OpenAPI surface | **CV endpoints** | 90% | 65% | 🟠 |
| F10 | Telegram commands | 22-command registry over existing services | — | 95% | 70% | 🟠 |

Readiness is scored against *implementability by an engineer who did not write the docs*, which is
the only meaningful reading under the 30-engineer premise.

---

## 3. Documentation Coverage Matrix

✓ complete · ⚠ partial · ✗ missing · — n/a

| Feature | PRD | SAD | ADR | Data Model | Contracts | Tasks | Tests | NFR | Decision Cov. | Status | % |
|---|---|---|---|---|---|---|---|---|---|---|---|
| F0 | ✓ | ✓ | — | ✓ | — | ✓ | ✓ | ✓ | ⚠ | ⚠ | 92 |
| F1 | ✓ | ⚠ | ✓ | ⚠ | ✓ | ✓ | ✓ | ✓ | ⚠ | ⚠ | 88 |
| F2 | ⚠ | ✓ | ✓ | ⚠ | — | ✓ | ✓ | ✓ | ⚠ | ⚠ | 85 |
| F3 | ✓ | ⚠ | ✓ | ✓ | ⚠ | ✓ | ⚠ | ⚠ | ✓ | ⚠ | 87 |
| F4 | ✓ | ⚠ | ✓ | ⚠ | ⚠ | ✓ | **✗** | ✓ | ⚠ | **✗** | 78 |
| F5 | ✓ | ✓ | ✓ | ⚠ | ⚠ | ✓ | ✓ | ✓ | ⚠ | ⚠ | 90 |
| F6 | ✓ | ✓ | ✓ | ⚠ | **✗** | ✓ | ⚠ | ✓ | ⚠ | **✗** | 80 |
| F7 | ✓ | ✓ | ✓ | ⚠ | — | ✓ | ✓ | ✓ | ⚠ | ⚠ | 88 |
| F8 | ✓ | ✓ | ✓ | ⚠ | ✓ | ✓ | ✓ | ✓ | ⚠ | ⚠ | 89 |
| F9 | ✓ | ✓ | ✓ | ✓ | **⚠** | ✓ | ✓ | ✓ | ⚠ | ⚠ | 85 |
| F10 | ✓ | ✓ | ✓ | ⚠ | ✓ | ✓ | ✓ | ✓ | ⚠ | ⚠ | 90 |

**Column notes.** *Data Model ⚠* = contradicts the global model. *Decision Coverage ⚠* = open
decisions still listed as blocking this feature's tasks. *F4 Tests ✗* = AC-12 and AC-13 have no test
rows. *F6 Contracts ✗* = the transition matrix is structurally incomplete.

---

## 4. Requirements Traceability Matrix

### 4.1 Invariants → enforcement

| Inv | Statement | Data model | Test | Status |
|---|---|---|---|---|
| 1 | RawPosting immutable | `uq_raw_postings_dedup` | F1 AC-10 arch test | ✓ Complete |
| 2 | One Fingerprint one Job | `uq_jobs_fingerprint` | F2 dedup corpus | ✓ Complete |
| 3 | One Enrichment/Match per (job,run) | `uq_enrichments_job_run`, `uq_matches_*` | F3 AC-06, F4 AC-01 | ✓ Complete |
| 4 | Every Score/Enrichment/Match has a reason | schema `minItems:1` + ctor guard | F3 AC-02, F4 AC-02 | ✓ Complete |
| 5 | Every claim cites a URL | **conflicting**: `source_url NOT NULL` (global) vs `source_id FK` (F8) | F8 uncited-claim suite | **⚠ Broken** |
| 6 | Ceiling checked pre-submission | `runs.ceiling_usd` snapshot | F3 QG-2 absence assertion | ✓ Complete |
| 7 | Never applies | — (negative) | no test asserts absence of an apply path | ⚠ Partial |
| 8 | Delivery idempotent | `uq_delivery_log` | F5 QG-2 | ✓ Complete |
| 9 | Single Owner, **no role model** | no tenant column | — | **⚠ Broken** — F10 `CommandScope { Owner, Operator }` |
| 10 | Robots / Retry-After / budgets | `job_sources.requests_per_second` | F1 AC-06/07 | ✓ Complete |
| 11 | No silent suppression | `scores.suppression_reason` | F7 QG-2, F4 AC-05 | ✓ Complete |
| 12 | Secrets never in repo/image/log | — | gate G6 `SecretRedactionTests` | ✓ Complete |

### 4.2 Acceptance criteria with broken downstream traceability

| AC | Requirement | Break |
|---|---|---|
| F1 AC-12 | Registry mutation requires operator scope | No F1 task implements an endpoint. Implemented in F9 T07; asserted in F1's test plan; F1 epic DoD claims "AC-01…AC-12 covered". **Cross-feature orphan.** |
| F3 AC-12 | Start / resume / **abort** a Run is operator-scoped | F9 contract has `resume` and `redeliver`. **No start endpoint, no abort endpoint anywhere.** AC untestable. |
| F4 AC-12 | Factual exclusion recorded and counted | **No row in F4 test-plan §AC coverage.** Epic DoD says "AC-01…AC-11". |
| F4 AC-13 | Calibration pass matches everything | **No test row.** Only T12's "Done when" mentions the bypass flag. |
| F4 AC-06/07 | CV owner-scoped upload/read | **CV endpoints absent from the F9 OpenAPI contract**, which SAD §4 S6 says is asserted against registered endpoints by a test. Either the test fails or a personal-data endpoint ships undocumented. |
| F2 — | Authorization coverage | DoD self-check claims authorization coverage "via §6.1 reprocess scope". **There is no authorization AC in F2.** |
| F5 AC-11 | Unreachable apply destination not presented | Design (SAD D3, T04) presents cards whose verification *timed out*. A timed-out link is unreachable. **AC and design disagree.** |

### 4.3 Business requirement → chain

| BR | Feature | ADR | Data model | Contract | Tasks | Tests | Status |
|---|---|---|---|---|---|---|---|
| Discover jobs early from ATS | F1 | 0009, F1-0001/2 | ✓ | ✓ | T01–T12 | ✓ | ✓ |
| One card per real opening | F2 | F2-0001 | ⚠ | — | T01–T09 | ✓ | ⚠ |
| Bounded, predictable LLM cost | F3/F4 | 0005, F3-0002, F4-0003 | ✓ | ⚠ stale | ✓ | ⚠ stale | **⚠** |
| Explainable ranking | F4/F7 | F4-0001, F7-0001/2 | ✓ | ✓ | ✓ | ✓ | ✓ |
| CV crosses one boundary | F4 | — | ✓ | ✓ | T10 | ✓ | ✓ |
| 07:00 always | F5 | F5-0001 | ✓ | ✓ | T09 | ✓ | ✓ |
| Never apply on Owner's behalf | F6 | F6-0001 | ✓ | ✓ | ✓ | **no negative test** | ⚠ |
| Recover without a terminal | F9/F10 | — | — | ⚠ paths wrong | ✓ | ✓ | **⚠** |
| Restore after data loss | ops | — | — | — | **none** | **none** | **✗** |

---

## 5. Cross Document Consistency Report

### 5.1 Global data model vs feature data models

`docs/architecture/data-model.md` header: *"The authoritative schema… Per-feature `data-model.md`
files refine the tables they own and must not redefine tables owned elsewhere."* Eight features
redefine, not refine.

| Table | Global says | Feature says | Consequence |
|---|---|---|---|
| `companies` | no `source` column | F1: `source NOT NULL` (`Curated`/`DirectoryCrawl`/`Manual`) | ADR-F1-0001's revert-by-provenance mechanism has no column globally |
| `raw_postings` | no `last_seen_at` | F1: `last_seen_at NOT NULL`, bumped on unchanged re-fetch | **AC-02 is unimplementable against the global schema** |
| `jobs` | 20 cols; `status ∈ Live/Closed/Quarantined` | F2: + `fingerprint_version`, `posted_at_granularity`, `is_tier2`; `status ∈ Live/Closed` | Enum mismatch; three columns invisible to the authoritative model |
| `jobs.employment_type` | `FullTime/Contract/PartTime/Unknown` | F2 adds `Internship` | Enum divergence |
| `job_aliases` | `seen_at` | F2: `first_seen_at` + `last_seen_at` | **Closure logic depends on per-alias `last_seen_at`; global has neither** |
| `runs` | no `jobs_carried_over` | F3: present | AC-09 carry-over count unrepresentable globally |
| `batches` | no `item_count` | F3: present | |
| `batch_items` | no `retry_count` | F3: present | **AC-08 once-only retry unimplementable globally** |
| `enrichments` | no `salary_confidence` | F3: present | **ADR-F4-0003's salary rule keys on `salary_confidence ≥ 0.8`** |
| `digests` | `excellent_matches`, `market_note` | F5: `strong_matches`, no `market_note`, + 6 cols | Column *renames*, not additions |
| `digest_cards` | no `apply_url_verified` | F5: present | AC-11 |
| `applications` | 7 cols | F6: + `posting_closed`, `last_reminder_condition`, `last_reminder_at`, `created_at` | AC-05/AC-07 unimplementable globally |
| `research_claims` | `source_url text NOT NULL` | F8: `source_id uuid NOT NULL FK → research_sources` | **Structurally different. Invariant 5 has two incompatible encodings.** |
| `research_sources` | **absent** | F8: owns it | Table missing from the authoritative model |
| `suppressions` | named `suppressions` | F7: `suppression_overrides` | Two names, one table |
| `command_invocations` | **absent** | F10: owns it | F10 has no entry in §1 or §6 at all |

### 5.2 Canonical vocabulary violated

`CLAUDE.md`: *"The canonical vocabulary is docs/CONTEXT.md — use those words and only those words."*

- **`ApplicationStatus`** — CONTEXT §1: `New → Saved → Applied → Interview → Rejected | Offer` (6).
  F6 data model, contract and matrix all use **7**, adding `Ignored`. F6 §4 argues `Ignored` is
  load-bearing preference evidence. It is not in the canonical enum.
- **`Signal`** — CONTEXT §1: `Ignored, Saved, Applied, Opened, Dismissed`. F7 `signals.kind`:
  `Opened, Ignored, Saved, Applied, Interview, Offer, Rejected, Rated`. **`Dismissed` exists nowhere
  in any feature; four kinds F7 requires exist nowhere in CONTEXT.**
- **Invariant 9** — "no role model". F10 introduces `CommandScope { Owner, Operator }`. The PRD notes
  the Owner *is* the operator, but the type is a role model and the invariant is unqualified.
- **`Run.state`** — system SAD §6.2 creates `Run{state=Discovering}`. `Discovering` is not in the
  enum (F3 data model, F3 SAD §6.1 state diagram, global data model all agree on 9 values, none of
  which is `Discovering`).

### 5.3 Cost figures — six stale sites after correction

Verified ground truth: enrichment $0.43 · matching $0.44 optimised ($1.58 naive) · Run $1.03 ·
ceiling $2.00.

| Location | Says | Should say |
|---|---|---|
| `00-overview/sad.md:137` (S4) | "keeps a Run under **$0.50**" | ≈$1.03 |
| `f3/index.md:45` | "a Run costs under **$0.50**" | ≈$1.03 (and this conflates the enrichment *stage* with the whole Run) |
| `f3/tasks/_epic.md:74` | "Enrichment cost under **$0.15**" | <$0.50 (PRD NFR), actual $0.43 |
| `f3/test-plan.md:114` | "Cost < **$0.15** for enrichment" | as above |
| `f4/tasks/T05:17` | "Matching cost stays under **$0.35**" | <$0.60 (NFR), actual $0.44 |
| `f4/test-plan.md:133` | "Matching cost < **$0.35**" | as above |

Additionally `f3/contracts/enrichment-schema.md:1688` reads *"That total sits above the intended
operating point and only just **under** the $2.00 ceiling"* — describing **$2.16**, which is *over*
$2.00. The sentence contradicts the table two lines above it.

### 5.4 Other cross-document contradictions

| # | Contradiction |
|---|---|
| a | **Crash matrix size.** System SAD §10 QG-2: "**six** checkpoints". F3 SAD §10, F3 test-plan, ADR-F3-0001, CLAUDE.md: **eight**. |
| b | **Transition matrix size.** F6 T01: "all **49** status pairs". F6 SAD §5, contract §Transition matrix, test-plan intro: "all **36**". F6 test-plan §The transition matrix suite: "**49** pairs including self-transitions". 7 statuses → 49 is correct. **The contract's own table is 7 rows × 6 columns = 42 cells; the `New` column is missing entirely**, so `X → New` is undefined for every X. |
| c | **Architecture rule count.** F0 T12 "What" enumerates **7**; T12 "Done when" and F0 epic say "**eight** rules"; F0 test-plan shows one test "**+ 5 sibling rules**" = 6; readiness G5 names **4**; coding-standards §2 table has **8**. |
| d | **`CostAborted` terminality.** F3 SAD §6.1 state diagram: `CostAborted --> Reporting`. Same file, next paragraph: "Only `Delivered`, `Failed` and `CostAborted` are terminal." `idx_runs_resumable` excludes `CostAborted` — so a cost-aborted Run that still owes a digest **will not be resumed on startup**. |
| e | **F1 insert semantics.** F1 SAD §6.1 sequence: `INSERT raw_posting ON CONFLICT DO NOTHING`. ADR-F1-0002, F1 data model and T11 all specify `ON CONFLICT … DO UPDATE SET last_seen_at`. `DO NOTHING` never bumps `last_seen_at`, **breaking AC-02 and the closure sweep that depends on it**. |
| f | **`MATCHES \|\|--\|\| SCORES`** (1:1 mandatory) in both the global ERD and F4's ERD. ADR-F4-0003 requires pre-filtered jobs to have a `scores` row **with no match**. The ERD forbids the design. |
| g | **D11 vs reality.** DECISION-LOG D11 ✅: *"Unlike sentra, there is **no** bilingual `.uk.md` tier here."* `docs/DECISIONS-MATRIX.uk.md` is 719 lines of Ukrainian, indexed in `docs/README.md` and cited by CLAUDE.md. |
| h | **ADR-0005 cites `O13`.** `ARCHITECTURE-OPEN-DECISIONS.md` defines O1–O12 only. The wikilink resolves (file exists) so the link checker passes; the anchor is fiction. |
| i | **Runbook paths.** R1 calls `POST /api/admin/runs/{id}/redeliver` and `/api/admin/runs/{id}/resume`. F9 contract defines `POST /api/runs/{id}/resume` and `/redeliver` — no `/admin` segment. **The documented recovery commands 404.** |
| j | **Runbook R4 SQL** filters `WHERE l.fetched_at > now() - interval '6 hours'`. `source_fetch_log` has `started_at`, not `fetched_at` (F1 data model). The query errors. |
| k | **Security policy vs ADR-0014.** ADR-0014: *"The `sub` claim must match the configured Owner subject — a valid token for a different subject is a 403."* `security.md` §2 reference policy applies `.RequireClaim("sub", …)` **only to `admin`**; the `read` policy checks scope alone. A valid `jobhunter:read` token for any other realm subject is admitted. |
| l | **Instrument count.** F0 T11: "the **seven** domain instruments from observability §2". Observability §2 declares **eight**. |
| m | **F0 dependency graph.** Tracker table: T13 deps = `T12`; T14 deps = `T10`. Graph: `T14 --> T13`. Table and graph disagree on whether T13 depends on T14. |
| n | **`JobHunter.TestKit`.** Required by F0 T02, F1/F2/F3/F4 test plans. **Absent from F0 SAD §5's project tree and from T01's "twelve projects plus the four test projects".** T01 also wikilinks `../sad` (F0 SAD, 9 projects) while saying "twelve projects", which is the *system* SAD's count. |
| o | **Ollama.** ADR-0005 designates it the cheap-tier fallback; F3 SAD §3 lists it as an external dependency; readiness §5 pins it in the stack baseline. **No task in any tracker implements an Ollama adapter.** F0 T04 only provisions the container for local dev. |
| p | **Near-duplicate grouping ownership.** ADR-F2-0001: "computed at **digest assembly**" (F5). F2 T08 implements `NearDuplicateGrouper` in F2. Two owners, no precedence. |
| q | **F2 write scope.** F2 SAD §2: "F2 reads `raw_postings` and writes `jobs`; **it never writes an F1 table**." F2 T09 implements "the retention job pruning raw payloads older than 90 days" — deleting from F1's table. |
| r | **Event catalog rule 4** ("Every event carries… `OccurredAt` **always**") vs the catalog's own payload column: `SourceFetchRequested`, `JobNormalized`, `EnrichmentCompleted`, `MatchingCompleted`, `RankingCompleted`, `JobIndexRequested` list no `OccurredAt`. |
| s | **`JobIndexRequested` "Published by: `RankingHandler`, `JobClosed`."** `JobClosed` is an event, not a publisher. |
| t | **Queue naming rule.** §1.6: queue = `{MessageType.FullName}.jobhunter-worker`. `DigestReady` is consumed by `JobHunter.Telegram`, which would own a queue suffixed `-worker`. |
| u | **`JobDuplicateDetected`** idempotency key `(JobId, RawPostingId)`; its payload fields are `CanonicalJobId`, `DuplicateRawPostingId`. The key names no field that exists. |
| v | **O3 retention.** Listed as an *open* decision blocking F2 T09; global data model and F1 data model both state "**Retention:** 90 days" as settled fact; F2 T09 implements it. |
| w | **O8 card count.** Open, "blocks F5 T6"; F5 SAD §6.1 and T03 both hard-code "score ≥ 70, limit 10". |

### 5.5 Open-decision register is stale in both directions

| # | Register says | Reality | Blocks pointer |
|---|---|---|---|
| O1 | Open, blocks F1 T3 | **Resolved by ADR-F1-0001** ("Resolves O1") | T03 ✓ correct |
| O2 | Open, blocks F9 **T8** | Still open | ✗ T08 is reconcile/rebuild; internet-facing is T04 |
| O3 | Open, blocks F2 T9 | Stated as fact in 2 data models | ✓ |
| O4 | Open, blocks F8 **T2** | **Resolved by ADR-F8-0001** | ✗ T02 is persistence; fetchers are T03/T04 |
| O5 | Open, blocks F7 **T5** | Still open | ✗ T05 is the learner; the floor is T07 |
| O6 | Open, blocks F2 T6 | **Resolved by ADR-F2-0001** ("Resolves O6") | ✓ |
| O8 | Open, blocks F5 **T6** | Fixed in F5 SAD/T03 | ✗ T06 is escaping; selection is T03 |
| O10 | Open, blocks F4 **T7** | **Resolved by ADR-F4-0002** ("Resolves O10") | ✗ T07 is ScoreCalculator; re-staling is T09 |
| O12 | Open, blocks F9 T9 | Fixed in F9 T09 | ✓ |

**Four decisions are resolved by accepted ADRs and still listed as open. Five of nine "Blocks"
pointers name the wrong task.** `BACKLOG.md` §6 lists only O1, O2, O4, O5 as needing an answer —
omitting O3, O6, O8, O9, O10, O12, which the register says also block tasks.

### 5.6 Estimate arithmetic — 11 of 11 epics disagree with their own tracker

| F | Tracker (arithmetic verified correct) | Epic claims | Δ |
|---|---|---|---|
| F0 | 8.0 | 9 | +1.0 |
| F1 | 7.5 | 8 | +0.5 |
| F2 | 5.25 | 6 | +0.75 |
| F3 | 7.75 | 9 | +1.25 |
| F4 | **stale header** | 9 | — |
| F5 | 6.25 | 8 | +1.75 |
| F6 | 3.75 | 5 | +1.25 |
| F7 | (n/a) | — | — |
| F8 | 5 | 6 | +1.0 |
| F9 | 5 | 6 | +1.0 |
| F10 | 5.5 | 6 | +0.5 |

Every epic rounds up; none states that it does. **F4's tracker header reads "11 tasks · 0×S + 8×M +
3×L ≈ 7 person-days" above a table of 13 rows** (correct: 10×M + 3×L ≈ 8) — the header was not
updated when T12/T13 were added.

`docs/README.md` says "**120 tasks across 11 features**" and lists F4 as **11**; its table therefore
sums to **118**. The filesystem has **120**. F4's `index.md` also says 11.

---

## 6. Feature Readiness Report

### F0 — Platform Foundation · 85
PRD defines behaviour fully; SAD implements it; 11 ACs with all five coverage types. **Gaps:**
`JobHunter.TestKit` is required by five features and appears in no project list (§5.4-n); the
architecture-rule count is stated three ways (§5.4-c); tracker table and graph disagree on T13/T14
(§5.4-m); T11 cites seven instruments where eight exist. F0 is the only feature whose failure blocks
all others, so these should be cheap to fix and are worth fixing first.

### F1 — ATS Job Discovery · 70
Strongest contract document in the set (`ats-endpoints.md` names real paths, real quirks, volatile
fields per provider). **Blockers:** the SAD sequence specifies `ON CONFLICT DO NOTHING`, which breaks
AC-02 (§5.4-e); AC-12 has no implementing task in F1 (§4.2); `JobClosed` is declared a F1 output in
the epic and the event catalog names `DiscoveryHandler` as publisher, **but no F1 task produces it**
— F2 T08 does. **Missing:** an NFR or test for the 50-company detection set's *construction*; the set
is referenced but its provenance is not specified.

### F2 — Normalization & Dedup · 65
The dedup corpus is the best-specified test artifact in the repository — 11 labelled categories with
adversarial cases over-represented, and "the corpus grows by defect". **Blockers:** four column-level
contradictions with the global model (§5.1) including `job_aliases`, on which closure logic depends;
T09 violates the feature's own stated write scope (§5.4-q); grouping ownership is ambiguous (§5.4-p).
**Missing:** no authorization AC despite the DoD self-check claiming one (§4.2).

### F3 — Claude Batch Enrichment · 75
The Run-as-aggregate design is correct and the three rules that make it work (persist
`provider_batch_id` first, unique `(run_id, stage, tier)`, upsert on natural keys) are exactly right.
The QG-2 absence assertion — a fake client that throws on `SubmitAsync`, test passes only if never
invoked — is a genuinely strong test design. **Blockers:** `CostAborted` is simultaneously terminal
and non-terminal, and the resume index excludes it (§5.4-d); `Discovering` state in the system SAD
does not exist (§5.2); AC-12 needs start/abort endpoints that exist nowhere (§4.2); Ollama fallback
has no task (§5.4-o). **Stale:** two $0.15 sites.

### F4 — CV Matching & Ranking · 55 🔴 **lowest**
The CV boundary is the one genuinely damaging failure mode in the system and it is handled well:
single render site, no logger on the prompt builder, pass-by-value, sentinel scan with no allowlist
that also runs at `Debug` and on forced-failure paths. That part is ready.

The cost decision is not. ADR-F4-0003 is sound and its three safety properties (recorded suppression,
`MatchAllJobs` bypass, weekly regret sampler) are the right mitigations — but the ADR was
**integrated into only half the feature**:

- **AC-12 and AC-13 have no test rows** in the test plan.
- The epic DoD and "Upstream" both say "AC-01…**AC-11**".
- **ADR-F4-0003 is missing from F4 SAD §9's decision table** (referenced in S7, absent from the register).
- The tracker header still describes 11 tasks (§5.6).
- Two `$0.35` assertions contradict the $0.60 NFR.
- The ERD forbids the score-without-match rows the ADR requires (§5.4-f).
- `match-schema.md` — the authoritative prompt contract — **records neither the `cache_control`
  breakpoint nor the "nothing volatile before the breakpoint" constraint** that the cost model now
  depends on. That constraint lives only in the ADR and T13.
- **Rule duplication with no precedence:** timezone-incompatible and employment-type-not-sought appear
  *both* as pre-match filter rules (ADR-F4-0003) *and* as post-ranking suppression rules
  (`match-schema.md` §Suppression). Whether a job is excluded before matching or suppressed after is
  undefined.
- T12 asserts "pass rate lands in the 35–50% band on **the reference corpus**". **No reference corpus
  is defined** in the test plan's §Test data.

### F5 — Daily Digest & Telegram · 80
The best-realised feature. Four degraded-day variants each with committed copy; the delivery-log
design correctly reasons about the one-statement crash window and chooses at-least-once deliberately;
`Won't show similar` is specified as contract text with its rationale. **Gaps:** AC-11 and the
verification design disagree (§4.2); `/stats` ships here and **vanishes from F10's catalogue** with no
deprecation note; four commands are claimed by three features (§8).

### F6 — Application Tracking · 60 🔴
ADR-F6-0001's asymmetry argument (permissive transitions + immutable history, refusing only
impossible sequences, every refusal naming a remedy) is correct and well-reasoned. **Blockers:** the
transition matrix — the feature's centrepiece — is specified at three different sizes and **the
contract table omits the `New` column entirely** (§5.4-b); `Ignored` is not in the canonical enum
(§5.2); five API endpoints are defined with **no build-order dependency on F9**, which owns the API
host.

### F7 — Preference Learning · 70
The indifferent-profile test ("must produce *no* weights") is the test that separates learning from
superstition, and specifying it explicitly is a mark of quality. Evidence floor as a constructor
guard is right. **Gaps:** `signals.kind` contradicts CONTEXT (§5.2); `suppressions` vs
`suppression_overrides` naming (§5.1); O5's blocks-pointer is wrong; `/hidden` is claimed by both
F7 T08 and F10.

### F8 — Company Research · 70
Fetch-then-synthesise with `source_id` as a NOT NULL FK is the strongest possible encoding of
invariant 5 — an uncited claim is unrepresentable, not merely rejected. `categories_unavailable`
("absence of information is information") is a good call. **Blockers:** the global model encodes
invariant 5 incompatibly (§5.1); `research_sources` missing from the global model; O4 still open
though ADR-F8-0001 resolves it; F8 writes `companies.stage` and `employee_band`, violating the
ownership rule (§8).

### F9 — Search & API · 65
Cursor pagination with the stated reason, fallback-deny, RFC 7807, generated-doc-vs-endpoints
conformance test — all correct. **Blockers:** **CV endpoints are absent from a contract that claims
to list every endpoint and is machine-asserted** (§4.2); no Run start/abort endpoints for F3 AC-12;
runbook paths don't match (§5.4-i); "owner-scoped" is used throughout F4/F6/F7 but only
`jobhunter:read`/`jobhunter:admin` exist.

### F10 — Telegram Commands · 70
The bidirectional conformance test is the best single idea in the newer material — *contract →
registry* is the direction normally missed and is exactly how a catalogue becomes fiction.
`ContractAnchor` makes it enforceable. Redis-TTL-over-sweeper is correctly reasoned. **Blockers:**
`CommandScope { Owner, Operator }` contradicts invariant 9 (§5.2); **internal contradiction on
`/cv`** — PRD §6.1 and SAD §2 say metadata only, data-model §Handoffs says "`/cv` via F4's **upload**
service"; `command_invocations` absent from the global model; command ownership overlaps four
features (§8). The catalogue has **22 headings**, described as "twenty commands" in the epic, PRD,
SAD D1 and `docs/README.md`.

---

## 7. Architecture Review

| Dimension | Assessment |
|---|---|
| **DDD / bounded contexts** | ✓ Nine stages with clean seams; `Domain` referencing only `Microsoft.Extensions.*.Abstractions` is enforced by test. |
| **Aggregates** | ✓ `Run` as a durable aggregate is the correct call and is argued properly against three alternatives. |
| **Repositories** | ✓ EF writes / Dapper reads, split enforced by an architecture test that greps for `ExecuteAsync`. |
| **CQRS** | ✓ Pragmatic — read models via Dapper, no event sourcing, stated as a deliberate limit. |
| **EDA** | ⚠ 24 events catalogued; **the §2 pipeline diagram omits 6 of them** and draws `OwnerActionRecorded → PreferenceModelUpdated`, which is false (the latter is a weekly Hangfire job, not a consequence of a tap). |
| **Dependency direction** | ✓ Stated identically in 3 places, enforced by F0 T12. |
| **Layering** | ✓ |
| **Message contracts** | ⚠ Rule 4 violated by 6 payloads; one publisher is an event; the queue-naming rule is wrong for the Telegram consumer. |
| **Worker responsibilities** | ✓ Single replica by design, with the scale-out path named (per-stage queues, manifest-only). |
| **Service boundaries** | ⚠ Three features write tables they do not own (F3 → `companies.stage`; F8 → `companies.stage`, `employee_band`; F2 T09 → deletes from `raw_postings`) against an explicit global rule forbidding it. F8 flags its own violation as "deliberate"; the global rule is not amended. |
| **Scalability** | ✓ Thresholds named (500 companies / 300 jobs), first bottleneck identified (Discovery fan-out), fix is manifests not code. |
| **Fault tolerance** | ✓ Per-stage isolation, quarantine-not-retry-harder, partial digest. |
| **Retry strategy** | ✓ Three layers (HTTP resilience, Wolverine per-handler, once-only item retry) with distinct rationales. |
| **Outbox / Inbox** | ✓ Both, with framework tables documented and a backlog alert. |
| **DLQ** | ⚠ Per-stage DLQ specified and alerted; **`replay-dlq` CLI appears in F0 SAD §5 and runbook R6 but no task implements it.** |
| **Compensation** | ✓ Ledger corrections are compensating entries, no update path. |
| **Idempotency** | ✓ **Strongest area.** Every consumer keyed, every key a real constraint, gate G4 requires a run-twice test per handler. |
| **Transactions** | ✓ State + outbox + signal in one transaction, stated per feature. |
| **Concurrency** | ✓ DB-as-arbiter (`ON CONFLICT`) rather than application locking, argued explicitly. |
| **Caching** | ⚠ Redis for buckets/state; the CV prompt cache is now load-bearing for the cost model but is documented only in an ADR and a task, not in the prompt contract. |
| **Configuration** | ✓ Options validated at startup with `ValidateOnStart`, one `DependencyInjection.cs` per project. |
| **Secrets** | ✓ Infisical, fail-fast, placeholders committed, redaction tests, rotation runbook. |
| **AuthN** | ✓ Keycloak OIDC + chat allowlist. |
| **AuthZ** | ⚠ **`sub` is enforced only on `admin` in the reference policy, contradicting ADR-0014.** Two scopes exist; four features say "owner-scoped", which maps to neither cleanly. |
| **Observability** | ✓ Label-cardinality discipline is explicit and correct (ids on spans, not labels). |
| **Logging** | ✓ Structured-only, never-log list, scrubbing processor as second defence. |
| **Metrics / Tracing** | ✓ / ✓ |
| **Health checks** | ✓ `/ready` deliberately excludes Anthropic and Typesense, with the reason stated. This is right and often got wrong. |
| **Deployment** | ✓ Kustomize + overlays + pre-deploy migrator Job + `Recreate` on singletons. |
| **Horizontal scaling** | ✓ API scales; worker and bot are singletons by design with the constraint asserted in a manifest test. |
| **Config management** | ✓ Terraform ConfigMap for non-secrets, Infisical for secrets, precedence stated. |

---

## 8. Cross Feature Analysis

**Duplicate functionality — the command surface.** Four commands have three or four claimants with no
precedence:

| Command | F5 T11 | F6 T09 | F7 T08 | F9 T09 | F10 |
|---|---|---|---|---|---|
| `/pipeline` | ✓ | ✓ | | | ✓ |
| `/note` | | ✓ | | | ✓ |
| `/search` | ✓ | | | ✓ | ✓ |
| `/hidden` | | | ✓ | | ✓ |
| `/saved` | ✓ | | | | ✓ |
| `/stats` | ✓ | | | | **dropped** |

`IMPLEMENTATION-READINESS` §3 says "`/start`, `/help` and `/digest` ship earlier with F5 T11" —
**three**. F5 T11 and the F5 message contract ship **seven**. F10's catalogue silently drops
`/stats`.

**Duplicate rules.** Timezone and employment-type exclusion exist as F4 pre-match filter rules *and*
F4 suppression rules *and* (learned variants) F7 weights. ADR-F4-0003 acknowledges the F7 overlap and
bounds it ("pre-match rules strictly factual, preference rules strictly learned") but never addresses
the overlap with F4's own suppression table.

**Duplicate models.** `suppressions` / `suppression_overrides`. `strong_matches` /
`excellent_matches`. `market_note` / `narrative`.

**Shared abstractions — good.** F3's Run/Batch/poller/ledger reused unchanged by F4, F5, F8 with "no
F3 file is modified" as an explicit DoD in F4 T05 and F7 T06. F5's formatter reused by F10 with an
architecture test forbidding handlers from formatting. This is the healthiest part of the
cross-feature design.

**Circular dependencies.** None in code. One in documentation: F4 SAD depends on F7's preference
component; F7 SAD §6.2 describes F4's `RankingHandler` behaviour. Consistent, but the
fitting/consuming boundary is described from both sides and could drift.

**Hidden dependencies.**

1. **F6 and F7 both define API endpoints with no build-order edge from F9**, which owns the host
   (`F9 T04 api-host-auth`). Build order has `F5 → F6`, `F5 → F7`, `F2 → F9` and no edge into F6/F7
   from F9.
2. **F5 T04** routes apply-link verification through F1's politeness handler, which enforces
   `robots.txt` — a robots-disallowed apply URL would be unverifiable, and the interaction is not
   addressed.
3. **F10 depends on F4's CV upload service** (data model §Handoffs) while declaring `/cv` read-only.

**Boundary violations.** Three write-outside-ownership cases (§7). One read-model duplication risk
flagged and mitigated by F10 D4 (both surfaces call the same query services).

**Missing reusable component.** Four features assert "no secret/CV/note content in logs" with
separate scan tests (G6, F4 T10, F6 T07, F10 audit). One shared artifact-scanner harness is implied
but never specified as shared.

---

## 9. Documentation Quality Review

**Strong.** Naming is consistent (`F{n}-{NNNN}` ADRs, `T{NN}-{kebab}` tasks, `{Feature}_{What}`
migrations). Folder structure is uniform across all 11 features. Frontmatter is complete on every
non-task file. Every ADR uses the same nine-section shape with 3–4 genuinely considered options and
an explicit rejection rationale — the rejections are argued, not listed. Rationale density is
unusually high: most design statements carry a "why", and several carry a "why not the obvious
alternative". 1,746 wikilinks all resolve.

**Diagram coverage.** C4 Context + C4 Container at system level; 4 system sequence diagrams;
per-feature sequences for every runtime path; ER diagrams per feature; two state diagrams (`Run`,
implied `Application`); one gantt; two build-order graphs; 11 task dependency graphs.

**Weak.**

| # | Gap | Detail |
|---|---|---|
| D-21 | Task files have no frontmatter | 120 of 260 files carry none, so they are invisible to any frontmatter-driven tooling the rest of the corpus assumes |
| D-22 | No state diagram for `Application` | The transition matrix is a table only, and it is the incomplete artifact (§5.4-b) |
| D-23 | Event catalog diagram incomplete | 18 of 24 events shown; one relationship is factually wrong |
| D-24 | No API examples for errors | Beyond one 409 and one 503 |
| D-25 | No global glossary of *scopes* | "owner-scoped", "operator scope", `jobhunter:read`, `jobhunter:admin`, `CommandScope.Owner`, `CommandScope.Operator` are used interchangeably across seven documents |
| D-26 | Currency mixing | `infrastructure.md` §8 totals £ and $ line items into "~$31.50/month" with no rate stated |
| **D-27** | **Mis-citation** | `infrastructure.md` §8 wikilinks `../engineering/deployment` under the alias *"caching applies to batch submissions"*; `deployment.md` says nothing about prompt caching. **Introduced in the session preceding this audit.** |
| **D-28** | **Ordering defect** | `DECISIONS-MATRIX.uk.md` places `D47a` and `D47b` at lines 589 and 606, **before** `D47` at line 621. **Introduced in the session preceding this audit.** |
| D-29 | `AddNpgsqlInstrumentation()` | Not the real Npgsql OpenTelemetry API surface |
| D-30 | `b.UseIdentityAlwaysColumns()` | Commented "no implicit sequences on uuid keys"; the method concerns integer identity columns and does nothing for `uuid` |

---

## 10. Risk Assessment

P = probability, I = impact, S = severity.

| # | Risk | Type | P | I | S | Mitigation |
|---|---|---|---|---|---|---|
| R-01 | Parallel teams write divergent migrations from the contradictory global model | Technical | **High** | **High** | 🔴 | Regenerate the global model from feature models mechanically; make it derived, not hand-maintained |
| R-02 | Work starts on tasks blocked by undecided architecture; rework | Business | **High** | High | 🔴 | Close O1/O4/O6/O10; re-gate the readiness matrix per task, not per feature |
| R-03 | Cost model wrong in code because assertions carry stale figures ($0.15/$0.35) | Business | **High** | Med | 🟠 | Single-source every cost constant to `PricingTable`; delete duplicated figures from tests |
| R-04 | Pre-match filter hides a job the Owner wanted | AI/Product | Med | **High** | 🔴 | Already designed: suppression row, bypass flag, regret sampler. **But AC-12/13 have no tests — the mitigation is unverified.** |
| R-05 | Prompt cache silently invalidated → bill doubles with nothing failing | Technical | Med | High | 🟠 | T13's `cache_read_input_tokens > 0` assertion. Must be promoted into `match-schema.md` as a contract constraint |
| R-06 | Unrecoverable data loss — no backup exists | Operational | Med | **Critical** | 🔴 | **No task creates the pg_dump job R9 restores from.** Backlog item only |
| R-07 | Token for a non-Owner subject reads all data | Security | Low | **High** | 🟠 | Apply the `sub` check to the `read` policy as ADR-0014 requires |
| R-08 | Recovery fails during an incident because runbook paths are wrong | Operational | **High** | Med | 🟠 | Fix R1 paths and R4 column; add a runbook↔contract conformance check |
| R-09 | ATS provider changes shape silently | Integration | **High** | Med | 🟠 | Well handled: weekly contract suite, per-adapter isolation, fixture-on-defect rule |
| R-10 | CV leaks into a log or span | Security | Low | **Critical** | 🟠 | Best-mitigated risk in the set: structural + sentinel scan, no allowlist, Debug level, forced-failure paths |
| R-11 | False merge hides a job permanently | Product | Low | High | 🟢 | Zero-false-merge corpus gate; asymmetry argued explicitly |
| R-12 | Batch SLA (24 h) makes digests routinely partial | Integration | Med | Med | 🟢 | Partial-digest policy; carry-over counted and shown |
| R-13 | Preference learning over-fits and narrows the digest | AI | Med | Med | 🟢 | 200-signal floor, 0.40 dimension bound, 3-card floor, one-tap disable |
| R-14 | Prompt injection from a job description | Security | Med | Low | 🟢 | Structural: schema-bound, no tools, typed columns, escaped output |
| R-15 | Single worker replica / single node — no HA | Operational | Med | Med | 🟢 | Accepted debt, documented, cost is one delayed digest |
| R-16 | Command surface fragments across 4 features | Technical | **High** | Med | 🟠 | Assign every command to exactly one feature before F5 T11 is written |
| R-17 | Estimates 15–33% understated per feature | Business | **High** | Med | 🟠 | Trackers are correct; fix the epics |
| R-18 | Ollama fallback assumed available, never built | Technical | Med | Med | 🟠 | Either add the task or strike it from ADR-0005 and the stack baseline |
| R-19 | 30 engineers on 500-LOC one-day PRs → merge contention | Operational | **High** | High | 🔴 | The plan is written for one person; a parallel work-breakdown does not exist |
| R-20 | Self-hosted runner is a single point of deploy failure | Deployment | Med | Low | 🟢 | Documented manual fallback |

---

## 11. Missing Artifacts

**Blocking**

1. **Backup task** — R9 restores from a nightly `pg_dump` to Azure Blob that no feature creates.
   Backlog item, no owner, no tracker row.
2. **CV endpoints in the F9 API contract** — required by F4 AC-06/AC-07 and F4 T03; absent from the
   document a test asserts against.
3. **Run start / abort endpoints** — F3 AC-12 requires all three; only `resume` and `redeliver` exist.
4. **F4 test rows for AC-12 and AC-13** — the pre-match filter and calibration pass are untested.
5. **Pre-match reference corpus** — T12 asserts a 35–50% pass rate against it; it is defined nowhere.
6. **Ollama adapter task** — designated fallback tier in ADR-0005, F3 SAD §3, readiness §5.
7. **`replay-dlq` CLI task** — referenced in F0 SAD §5 and runbook R6.
8. **`JobHunter.TestKit` project** — required by 5 features, in no project list.
9. **`JobClosed` publisher task** — declared an F1 output, no F1 task produces it.

**Important**

10. Cache-control constraint in `match-schema.md` (exists only in ADR + task).
11. `research_sources` and `command_invocations` in the global data model.
12. Scope glossary reconciling six auth vocabularies.
13. `Application` state diagram; complete transition matrix including the `New` column.
14. F2 authorization AC.
15. Negative test for invariant 7 (no apply path exists).
16. Shared artifact-scanner harness spec.
17. Parallel work-breakdown for >1 engineer (no artifact addresses concurrency of work).

---

## 12. Blockers

### 🔴 Critical — must be fixed before any code is written (9)

| # | Blocker | Evidence |
|---|---|---|
| C-1 | Readiness matrix certifies 11/11 "Ready" while 10 open decisions block 11 named tasks; 4 are already resolved by accepted ADRs; 5 of 9 blocks-pointers name the wrong task | `IMPLEMENTATION-READINESS.md` §1 vs `ARCHITECTURE-OPEN-DECISIONS.md` vs `BACKLOG.md` §6 |
| C-2 | "Authoritative" global data model contradicts 8 feature models on ~16 tables, including a structural difference in the encoding of invariant 5 | `architecture/data-model.md` §3 vs feature `data-model.md` |
| C-3 | Canonical vocabulary contradicted: `ApplicationStatus` (6 vs 7), `SignalKind` (5 vs 8, with `Dismissed` implemented nowhere), `Run.state` (`Discovering` undefined), invariant 9 vs `CommandScope` | `CONTEXT.md` §1/§3 vs F6/F7/F10 |
| C-4 | Transition matrix — F6's centrepiece — specified as 36/42/49; **the contract table omits the `New` column, leaving 7 of 49 pairs undefined** | `f6/contracts/application-api.md` §Transition matrix |
| C-5 | ADR-F4-0003 integrated into half of F4: no AC-12/13 tests, absent from SAD §9, stale tracker header, ERD forbids its rows, cache constraint not in the contract, filter/suppression precedence undefined | F4 SAD §9, test-plan, tracker, ERD, `match-schema.md` |
| C-6 | No backup exists; R9 disaster recovery restores from a file nothing creates | `runbooks.md` R9 vs `BACKLOG.md` §4 |
| C-7 | CV endpoints missing from a machine-asserted API contract; F3 AC-12's start/abort endpoints missing entirely | `f9/contracts/openapi.md` |
| C-8 | Ownership rule "no feature alters a table it does not own" violated three times, once against the violating feature's own stated constraint | `architecture/data-model.md` §6; F2 SAD §2 vs F2 T09; F3, F8 |
| C-9 | Cost figures contradictory in 6 places; one sentence states $2.16 is "under" a $2.00 ceiling | §5.3 |

### 🟠 High (11)

C-10 `sub` claim not enforced on `read`, contradicting ADR-0014 · C-11 runbook R1 paths 404, R4 SQL
references a non-existent column · C-12 F1 SAD `DO NOTHING` breaks AC-02 · C-13 `CostAborted` both
terminal and non-terminal; resume index excludes it · C-14 command ownership split across 4 features,
`/stats` silently dropped · C-15 F6/F7 endpoints have no build-order dependency on F9 · C-16 all 11
epic estimates disagree with their trackers · C-17 F4 tracker header says 11 tasks over 13 rows;
`docs/README` sums to 118 of 120 · C-18 Ollama fallback has no task · C-19 event catalog: rule-4
violations, event-as-publisher, queue-naming rule, wrong diagram edge · C-20 architecture rule count
stated as 4/6/7/8.

### 🟡 Medium (8)

C-21 `JobHunter.TestKit` in no project list · C-22 crash matrix 6 vs 8 · C-23 ERD
`MATCHES||--||SCORES` · C-24 D11 denies the existence of a file the README indexes · C-25 ADR-0005
cites non-existent O13 · C-26 F10 `/cv` internally contradictory · C-27 F5 AC-11 vs verification
design · C-28 near-duplicate grouping owned twice.

### 🟢 Low (3)

C-29 F0 tracker graph vs table on T13/T14 · C-30 instrument count 7 vs 8 · C-31 22 catalogue headings
described as "twenty" in 4 places.

---

## 13. Improvement Recommendations

**Immediate — before the first commit (est. 3–4 days)**

1. Re-run the readiness gate **per task**, not per feature. Close O1/O4/O6/O10 as ADR-resolved;
   correct the five wrong blocks-pointers; mark the 11 genuinely blocked tasks `[?]` in their
   trackers.
2. Make `architecture/data-model.md` **derived**. Generate it from feature models, or demote it to an
   index with a stated "feature models win" precedence. Do not leave two normative schemas.
3. Amend `CONTEXT.md` §1 for `ApplicationStatus` (+`Ignored`), `Signal` (drop `Dismissed`, add the
   four outcome kinds), and either add `Discovering` to `Run.state` or fix the system SAD.
4. Complete the F6 transition matrix to 7×7 and settle on 49 in all four documents.
5. Finish ADR-F4-0003's integration: AC-12/13 test rows, SAD §9 entry, tracker header, ERD
   cardinality, cache constraint into `match-schema.md`, filter-vs-suppression precedence, define the
   reference corpus.
6. Delete every duplicated cost constant; assert against `PricingTable` only.
7. Add tasks: backup job, CV endpoints, Run start/abort, `replay-dlq`, `JobClosed` publisher,
   `JobHunter.TestKit`, Ollama adapter (or strike Ollama from ADR-0005 and readiness §5).
8. Apply the `sub` check to the `read` policy.
9. Fix R1 paths and R4 column; add a runbook↔contract check to CI.

**Important — before M2 (est. 2 days)**

10. Assign each of the 22 commands to exactly one owning feature; state F5's minimal subset
    explicitly; decide `/stats`.
11. Add F9 → F6 and F9 → F7 build-order edges.
12. Correct the 11 epic estimates from their trackers; fix the F4 task count in three places.
13. Publish a scope glossary; reconcile "owner-scoped" with the two real scopes.
14. Fix the event catalog: `OccurredAt` on all payloads, `JobIndexRequested` publisher, queue naming
    for non-worker consumers, `JobDuplicateDetected` key names, the §2 diagram.
15. Reconcile the architecture-rule count and enumerate the rules once, in `coding-standards.md` §2,
    referenced everywhere else.

**Recommended**

16. Frontmatter on the 120 task files.
17. `Application` state diagram.
18. Negative test for invariant 7.
19. Shared artifact-scanner harness.
20. Fix D-27 (the `deployment.md` mis-citation in `infrastructure.md` §8) and D-28 (the `D47a`/`D47b`
    ordering in the decisions matrix).

**Optional**

21. State the £/$ rate in the cost table.
22. Correct `AddNpgsqlInstrumentation` and the `UseIdentityAlwaysColumns` comment.
23. If the 30-engineer premise is real, add a parallel work-breakdown — nothing in this corpus
    addresses concurrent authorship.

---

## 14. Overall Scorecard

| Dimension | Score | Basis |
|---|---|---|
| Documentation Quality | **86** | Uniform structure, high rationale density, 1,746 links resolving; loses points for task-file frontmatter, one mis-citation, incomplete diagrams |
| Architecture Quality | **84** | Idempotency, resumability and cost enforcement are genuinely well-designed; loses points for three ownership violations and event-contract drift |
| Traceability Quality | **61** | Invariant→constraint→test chains are excellent; AC→task→test chains break in 7 places and the open-decision register is stale in both directions |
| Feature Completeness | **79** | All 11 have the full artifact set; F4 and F6 have unspecifiable centrepieces |
| **Implementation Readiness** | **58** | 9 critical blockers, 2 of them (data model, transition matrix) making parallel work actively unsafe |
| Maintainability | **83** | Docs-first with gates, ADR discipline, corpus-grows-by-defect rule; the gate itself is unenforced |
| Scalability | **80** | Honest thresholds, named first bottleneck, manifest-only scale-out path |
| Testability | **88** | **Highest.** Absence assertions, proven-red fixtures, no allowlist on the security gate, indifferent-profile test, bidirectional conformance |
| Operational Readiness | **57** | 10 runbooks with real commands; two contain wrong paths/columns, and R9 restores a backup that does not exist |
| **Overall Project Quality** | **74** | Above-average design, below-threshold integration |

---

## 15. Final Verdict

# ❌ REJECTED

The design is not the problem. The design is better than most systems that would be reviewed at this
stage — invariants encoded as database constraints rather than prose, tests that assert absence
rather than state, ADRs that argue their rejections. Under the documented premise of one part-time
engineer, this corpus would work.

It is rejected against the audited premise because the connective artifacts have drifted from the
documents they govern, and **three of those artifacts make parallel work unsafe**: a global schema
contradicted by the feature schemas it claims to bind, a canonical vocabulary contradicted by the
features that must speak it, and a readiness gate that certifies work it simultaneously records as
blocked. Thirty engineers writing migrations and handlers concurrently against those three documents
will produce a schema that does not converge.

### Required before implementation may begin

**Gate 1 — Consistency (blocks all work)**

1. Resolve the global vs feature data-model contradiction across all ~16 tables; establish one
   normative source with stated precedence.
2. Reconcile `CONTEXT.md` with F6 `ApplicationStatus`, F7 `SignalKind`, F3 `Run.state`, F10
   `CommandScope`.
3. Complete the F6 transition matrix to 7×7; settle the count in all four documents.
4. Correct all six stale cost figures and the "$2.16 is under $2.00" sentence.
5. Resolve the three cross-feature write violations, either by amending the ownership rule or by
   relocating the writes.

**Gate 2 — Traceability (blocks the affected features)**

6. Re-run the readiness gate per task; close O1/O4/O6/O10; fix five blocks-pointers; mark blocked
   tasks.
7. Complete ADR-F4-0003's integration (7 items, §12 C-5) — **blocks F4**.
8. Add CV endpoints and Run start/abort to the F9 contract — **blocks F4, F3**.
9. Add AC-12/AC-13 tests and define the pre-match reference corpus — **blocks F4**.
10. Add F9 → F6 and F9 → F7 build-order edges — **blocks F6, F7**.

**Gate 3 — Missing work (blocks M1 exit)**

11. Create tasks for: backup job, `JobHunter.TestKit`, `replay-dlq`, `JobClosed` publisher, Ollama
    adapter (or strike it).
12. Apply the `sub` claim check to the `read` authorization policy.
13. Fix runbook R1 paths and R4 column reference.
14. Assign each of the 22 commands to exactly one feature; decide `/stats`.
15. Correct all 11 epic estimates and the F4 task count in three places.

Items 1–5 are approximately 3–4 days of documentation work with no code impact. On completion this
would be expected to reach **APPROVED WITH CONDITIONS**, with items 6–15 as conditions tracked
against M1.

---

**Documents analysed: 260 of 260.** Every feature verified. No document was skipped. Two findings
(D-27, D-28) are in material authored in the session immediately preceding this audit and are
reported at the same severity as the rest.

---

## Related

- [[IMPLEMENTATION-READINESS]] — the gate this audit assessed
- [[ARCHITECTURE-OPEN-DECISIONS]] · [[DECISION-LOG]] · [[BACKLOG]]
- [[CONTEXT]] · [[00-overview/sad|SAD]] · [[architecture/data-model|Global data model]]
