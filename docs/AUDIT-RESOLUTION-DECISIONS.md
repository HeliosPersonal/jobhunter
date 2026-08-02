---
status: Final
owner: "Viacheslav Melnichenko"
updated_at: "2026-08-02"
stage: "00"
tags: [audit, sdlc, resolution, canonical, jobhunter]
---

# Audit Resolution — Canonical Decisions

> This is the **single source of truth** for every value the SDLC audit found contradicted across
> documents. When any document disagrees with a value here, **this document wins** and the document is
> corrected to match. It exists so that parallel authors converge instead of drifting.
>
> Companion: [[SDLC-AUDIT]] (the findings), [[AUDIT-RESOLUTION-TRACKER]] (the parallel work plan).

---

## 1. Data-model precedence (resolves C-2, C-8, §5.1)

**Decision.** `architecture/data-model.md` is **demoted from "authoritative schema" to a derived
index.** Precedence rule, stated at the top of that file and honoured everywhere:

> **Feature `data-model.md` files are normative for the tables they own.** The global
> `architecture/data-model.md` is a *reconciled roll-up* of the feature models — a navigation aid, not
> a competing schema. Where they differ, the feature model wins and the global model is regenerated to
> match.

**Ownership table** (one owner per table — the only feature allowed to define/alter its columns):

| Table | Owner | Notes |
|---|---|---|
| `companies` | F1 | includes `source NOT NULL` (`Curated`/`DirectoryCrawl`/`Manual`) |
| `raw_postings` | F1 | includes `last_seen_at NOT NULL` (bumped on unchanged re-fetch) |
| `source_fetch_log` | F1 | column is `started_at` (not `fetched_at`) |
| `jobs`, `job_aliases` | F2 | `jobs` adds `fingerprint_version`, `posted_at_granularity`, `is_tier2`; `job_aliases` has `first_seen_at` + `last_seen_at`; `status ∈ {Live, Closed, Quarantined}`; `employment_type ∈ {FullTime, Contract, PartTime, Internship, Unknown}` |
| `runs`, `batches`, `batch_items` | F3 | `runs.jobs_carried_over`, `batches.item_count`, `batch_items.retry_count` are canonical |
| `enrichments` | F3 | includes `salary_confidence` (F4 salary rule keys on `≥ 0.8`) |
| `matches`, `scores` | F4 | a pre-filtered job MAY have a `scores` row with **no** `matches` row (see §6) |
| `digests`, `digest_cards` | F5 | canonical names: `strong_matches` (not `excellent_matches`), `narrative` (not `market_note`); `digest_cards.apply_url_verified` present |
| `applications` | F6 | adds `posting_closed`, `last_reminder_condition`, `last_reminder_at`, `created_at` |
| `signals` | F7 | see §2 for `kind` values |
| `suppression_overrides` | F7 | canonical name (not `suppressions`) |
| `research_sources`, `research_claims` | F8 | `research_claims.source_id uuid NOT NULL FK → research_sources` is the canonical encoding of invariant 5 (see §5) |
| `command_invocations` | F10 | must appear in the global roll-up §1 and §6 |

**Cross-feature write violations (C-8) — resolved by amending the ownership rule, not relocating code.**
The global rule is restated as:

> A feature may **write** a table owned by another feature only where explicitly whitelisted below.
> Every such write is an intentional, reviewed seam.

Whitelisted cross-owner writes:
- **F2 → `raw_postings`** (delete only): the 90-day retention prune (F2 T09). Owner F1 acknowledges.
- **F3 → `companies.stage`**: enrichment may set a company's pipeline stage. Owner F1 acknowledges.
- **F8 → `companies.stage`, `companies.employee_band`**: research synthesis writes firmographics.
  Owner F1 acknowledges.

F2 SAD §2's "never writes an F1 table" sentence is corrected to name this single whitelisted delete.

---

## 2. Canonical vocabulary (resolves C-3, §5.2)

`CONTEXT.md` §1 is **amended** to these values. All features already using them are correct; the
outliers listed are corrected to match.

**`ApplicationStatus`** — 7 states (add `Ignored`):
```
New → Saved → Applied → Interview → Rejected | Offer
Ignored   (terminal-ish; load-bearing preference evidence per F6 §4)
```
The legal-transition matrix is **7×7 = 49 pairs** (see §4).

**`Signal.kind`** — drop `Dismissed` (implemented nowhere); adopt F7's set:
```
Opened, Ignored, Saved, Applied, Interview, Offer, Rejected, Rated
```
`Dismissed` is removed from CONTEXT §1. The five-value list in CONTEXT is replaced by this eight-value
list. Rationale: outcome signals (`Interview`, `Offer`, `Rejected`) and `Rated` are load-bearing
preference evidence; `Dismissed` was never implemented.

**`Run.state`** — 9 values, canonical (from F3 data-model, authoritative):
```
Created, Enriching, Matching, Ranking, Researching, Reporting, Delivered, Failed, CostAborted
```
`Discovering` **does not exist.** System SAD §6.2's `Run{state=Discovering}` is corrected to
`Run{state=Created}`.

**Invariant 9 / `CommandScope`** — invariant 9 ("single Owner, no role model") stands. F10's
`CommandScope { Owner, Operator }` is **renamed and reframed** as a capability tag, not a role:
`CommandCapability { Standard, Sensitive }`. `Sensitive` gates destructive/mutating commands behind an
extra confirmation; it is **not** a second identity. CONTEXT and invariant 9 gain one sentence noting
the Owner is the sole principal and `CommandCapability` is a per-command sensitivity flag.

---

## 3. Cost figures — single ground truth (resolves C-9, §5.3)

**Ground truth** (verified in the audit; every other figure in the corpus is corrected to these):

| Constant | Value | Source of truth in code |
|---|---|---|
| Enrichment stage cost | **$0.43** | `PricingTable` |
| Matching stage cost (optimised) | **$0.44** | `PricingTable` (naïve $1.58 — do not quote) |
| Full Run cost | **$1.03** | derived |
| Run cost ceiling | **$2.00** | `runs.ceiling_usd` |
| Enrichment NFR ceiling | **< $0.50** | F3 PRD NFR |
| Matching NFR ceiling | **< $0.60** | F4 PRD NFR |

**Corrections to apply** (each owner fixes its own files):

| File | Currently says | Correct to |
|---|---|---|
| `00-overview/sad.md` (S4, ~:137) | "keeps a Run under **$0.50**" | "keeps a Run under **$2.00** (≈$1.03 typical)" |
| `f3/index.md` (~:45) | "a Run costs under **$0.50**" | "the enrichment **stage** costs ≈$0.43; a full Run ≈$1.03 under the $2.00 ceiling" |
| `f3/tasks/_epic.md` (~:74) | "Enrichment cost under **$0.15**" | "Enrichment cost **< $0.50** (NFR); ≈$0.43 typical" |
| `f3/test-plan.md` (~:114) | "Cost < **$0.15** for enrichment" | "Cost **< $0.50** for enrichment (assert against `PricingTable`)" |
| `f4/tasks/T05` (~:17) | "Matching cost stays under **$0.35**" | "Matching cost **< $0.60** (NFR); ≈$0.44 typical" |
| `f4/test-plan.md` (~:133) | "Matching cost < **$0.35**" | "Matching cost **< $0.60** (assert against `PricingTable`)" |
| `f3/contracts/enrichment-schema.md` (~:1688) | "$2.16 … only just **under** the $2.00 ceiling" | "$2.16 is **over** the $2.00 ceiling and would be **aborted pre-submission**" (fix the sentence to agree with the table above it) |

**Rule going forward:** no test hard-codes a dollar figure. Every cost assertion references
`PricingTable` constants. Duplicated literals are deleted.

Currency (D-26): `infrastructure.md` §8 must state the £→$ rate used before summing £ and $ line items
into a monthly total.

---

## 4. F6 transition matrix (resolves C-4, §5.4-b)

**Decision.** 7 statuses → **7×7 = 49 pairs including self-transitions.** Settle on **49** in all four
places (F6 T01, F6 SAD §5, contract §Transition matrix, test-plan). The contract table must be a full
**7×7 grid with the `New` column present** (currently 7×6, `New` column missing → `X → New`
undefined). Add an `Application` state diagram (D-22).

Legal transitions follow ADR-F6-0001's asymmetry rule (permissive; refuse only impossible sequences;
every refusal names a remedy). Self-transitions are legal no-ops.

---

## 5. Invariant 5 encoding (resolves §5.1 `research_claims`, C-2)

**Canonical:** F8's `research_claims.source_id uuid NOT NULL FK → research_sources`. This is stronger
than `source_url text NOT NULL` — an uncited claim is *unrepresentable*, not merely rejected. The
global roll-up adopts `research_sources` and the FK encoding. The `source_url text NOT NULL` form is
removed from the global model.

---

## 6. MATCHES / SCORES cardinality (resolves C-23, §5.4-f)

**Decision.** Relationship is `MATCHES }o--|| SCORES` — a `scores` row MAY exist with **zero** matches
(the pre-filtered / suppressed case required by ADR-F4-0003). Both the global ERD and F4's ERD are
corrected from `||--||` (mandatory 1:1) to this. `scores` is owned by F4.

---

## 7. Open-decision register (resolves C-1, §5.5)

**Close as resolved-by-ADR** (mark `✅ Resolved`, cite the ADR, remove from "blocks"):
- **O1** → ADR-F1-0001
- **O4** → ADR-F8-0001
- **O6** → ADR-F2-0001
- **O10** → ADR-F4-0002
- **O3** → settled fact (90-day retention in 2 data models); close as resolved.
- **O8** → fixed in F5 SAD/T03 (score ≥ 70, limit 10); close as resolved.
- **O12** → fixed in F9 T09; close as resolved.

**Remain genuinely open** (keep, with corrected blocks-pointer):
- **O2** — internet-facing surface → blocks **F9 T04** (not T08).
- **O5** — evidence floor / weight floor → blocks **F7 T07** (not T05).

**Corrected blocks-pointers** (§5.5 right column): O2→T04, O4 was T02 now closed, O5→T07, O8 was T06
now closed, O10 was T07 now closed.

**Readiness gate re-run per task.** `IMPLEMENTATION-READINESS.md` stops certifying whole features
"Ready". It certifies **per task**. Tasks still blocked by O2 or O5 are marked `[?]`. All other tasks
whose only blocker was a now-closed decision become `[ ]` ready. `BACKLOG.md` §6 lists only O2 and O5
as needing an answer.

---

## 8. Command ownership (resolves C-14, C-31, §8)

**Decision.** The Telegram command surface is **22 commands** (not "twenty" — correct the four sites:
F10 epic, PRD, SAD D1, README). Each command has **exactly one owning feature**; every other feature
that "ships" it instead **registers against F10's registry**. F10 owns the registry and the catalogue;
feature tasks provide handlers.

| Command | Sole owner | Notes |
|---|---|---|
| `/start`, `/help`, `/digest` | **F5 T11** | the minimal bootstrap subset that must ship with the first digest |
| `/pipeline` | **F6** | F5/F10 register, do not implement |
| `/note` | **F6** | |
| `/search` | **F9** | F5/F10 register |
| `/hidden` | **F7** | F10 registers; F7 T08 keeps the handler |
| `/saved` | **F5** | |
| `/stats` | **F5** | **retained, not dropped**; F10 catalogue must list it |
| all remaining catalogue commands | **F10** | |

`IMPLEMENTATION-READINESS §3` is corrected: F5 T11 ships **`/start`, `/help`, `/digest`** (three named)
but the F5 message contract legitimately covers seven — reconcile the wording to "F5 T11 ships seven
commands, of which `/start`, `/help`, `/digest` are the bootstrap subset." F10's catalogue must not
drop `/stats`.

---

## 9. Scope glossary (resolves C-10, D-25, §Auth)

**Two real scopes only:** `jobhunter:read` and `jobhunter:admin`. Publish a glossary
(`engineering/security.md` or a new `SCOPE-GLOSSARY` section) mapping the six vocabularies:

| Phrase used in docs | Real mapping |
|---|---|
| "owner-scoped" (F4/F6/F7) | `jobhunter:read` **plus** the `sub` == Owner check |
| "operator scope" / "operator-scoped" (F1/F3) | `jobhunter:admin` |
| `CommandScope.Owner` → | (removed; see §2) `CommandCapability.Standard` |
| `CommandScope.Operator` → | `CommandCapability.Sensitive` |

**`sub` check (C-10):** ADR-0014 requires the `sub` claim to equal the configured Owner subject on
**every** policy, not just `admin`. `security.md` §2 reference policy applies `.RequireClaim("sub", …)`
to **both** `read` and `admin`. A valid `jobhunter:read` token for a different subject is a 403.

---

## 10. Event catalog (resolves C-19, §5.4-r/s/t/u, D-23)

- **Rule 4 (`OccurredAt` always):** add `OccurredAt` to the payload column of `SourceFetchRequested`,
  `JobNormalized`, `EnrichmentCompleted`, `MatchingCompleted`, `RankingCompleted`, `JobIndexRequested`.
- **`JobIndexRequested` publisher:** `JobClosed` is an event, not a publisher. Corrected to
  "Published by: `RankingHandler`, `ClosureSweepHandler`" (the handler that emits on closure).
- **Queue naming:** the `{MessageType.FullName}.jobhunter-worker` rule is generalised to
  `{MessageType.FullName}.{consuming-deployable}`, so `DigestReady` consumed by `JobHunter.Telegram`
  owns `…​.jobhunter-telegram`. Rule text updated.
- **`JobDuplicateDetected`:** idempotency key corrected to `(CanonicalJobId, DuplicateRawPostingId)` —
  fields that exist on the payload.
- **§2 pipeline diagram:** show all 24 events; remove the false
  `OwnerActionRecorded → PreferenceModelUpdated` edge (the model update is a weekly Hangfire job, not a
  consequence of a tap).

---

## 11. Architecture-rule count (resolves C-20, §5.4-c)

**Canonical count: 8 architecture rules.** Enumerate them **once** in `coding-standards.md` §2; every
other site (F0 T12 "What" and "Done when", F0 epic, F0 test-plan, readiness G5) references that list
and states **8**. The F0 test-plan "+ 5 sibling rules" is corrected to "+ 7 sibling rules" (1 shown + 7
= 8).

---

## 12. Crash matrix count (resolves C-22, §5.4-a)

**Canonical: 8 checkpoints.** System SAD §10 QG-2 "six" is corrected to **eight**, matching F3 SAD §10,
F3 test-plan, ADR-F3-0001 and CLAUDE.md.

---

## 13. `CostAborted` terminality (resolves C-13, §5.4-d)

**Decision.** `CostAborted` is **terminal** *for the cost path* but a Run that still owes a digest must
still be reportable. Resolution: on cost-abort the Run transitions `CostAborted` and emits
`RunCostAborted`, which F5 consumes to send the degraded/aborted digest **synchronously in that same
flow** — no resume needed. Therefore:
- Remove the `CostAborted --> Reporting` edge from the F3 SAD §6.1 state diagram (it is terminal).
- Keep `idx_runs_resumable` excluding `CostAborted` (correct — it is terminal).
- The reporting obligation is discharged by the `RunCostAborted` event handler, documented in F3 SAD
  §6.1 prose and F5.

---

## 14. F1 insert semantics (resolves C-12, §5.4-e)

**Canonical:** `INSERT … ON CONFLICT … DO UPDATE SET last_seen_at = excluded.last_seen_at` (from
ADR-F1-0002, F1 data-model, T11). F1 SAD §6.1 sequence's `DO NOTHING` is corrected to `DO UPDATE`.
`DO NOTHING` never bumps `last_seen_at` and would break AC-02 and the closure sweep.

---

## 15. Near-duplicate grouping ownership (resolves C-28, §5.4-p)

**Decision.** `NearDuplicateGrouper` is **computed at digest assembly (F5)**, matching ADR-F2-0001.
F2 T08 is **removed / relocated to F5** (the grouper moves to F5's assembly step). F2's tracker and epic
drop the grouper task; F5 gains it. ADR-F2-0001 wording ("computed at digest assembly") is authoritative.

---

## 16. AC-11 vs verification design (resolves C-27, §4.2, §5.4)

**Decision.** A card whose apply-link verification **timed out** is treated as **unverified**, and
`digest_cards.apply_url_verified = false` cards are still shown **but flagged** ("link unverified"),
*except* a link that verified as **unreachable (4xx/5xx/DNS)** is not presented (AC-11). Timeout ≠
confirmed-unreachable. F5 SAD D3/T04 and AC-11 are reconciled to this: present-with-flag on timeout,
suppress on confirmed-unreachable. F5 T04 also documents the robots-disallowed apply URL case
(unverifiable → shown with flag).

---

## 17. New tasks to add (resolves C-6, C-7, C-18, §11 blocking)

Each becomes a real tracker row + task file under the named feature. IDs assigned here to avoid
collision:

| New task | Feature | ID | What |
|---|---|---|---|
| Backup job | F0 | **F0 T15** | nightly `pg_dump` → Azure Blob (what R9 restores from) |
| `JobHunter.TestKit` project | F0 | **F0 T01** (amend) | add to project tree + T01's project list; required by 5 features |
| `replay-dlq` CLI | F0 | **F0 T16** | the CLI referenced in F0 SAD §5 and runbook R6 |
| `JobClosed` publisher | F1 | **F1 T13** | the F1 task that actually emits `JobClosed` (declared an F1 output) |
| Ollama fallback adapter | F3 | **F3 T14** | cheap-tier fallback adapter (ADR-0005). *Decision: keep Ollama; build the adapter.* Numbered T14 (T13 already exists). |
| CV endpoints | F9 | **F9 contract + F9 T05** | `POST /api/cv`, `GET /api/cv` (owner-scoped) in openapi.md; required by F4 AC-06/07 & T03. Folded into existing T05. |
| Run start / abort endpoints | F9 | **F9 contract + F9 T06** | `POST /api/runs`, `POST /api/runs/{id}/abort` (operator/admin) for F3 AC-12. Folded into existing T06. |
| Near-duplicate grouper (relocated) | F5 | **F5 T13** | moved from F2 T08 to F5 digest assembly per ADR-F2-0001 (§15). |
| AC-12 / AC-13 test rows + reference corpus | F4 | **F4 test-plan** | pre-match filter + calibration tests; define the 35–50% reference corpus in §Test data |

Runbook paths (C-11): R1 uses `/api/runs/{id}/redeliver` and `/api/runs/{id}/resume` (no `/admin`
segment — match F9 contract). R4 SQL uses `started_at` (not `fetched_at`).

---

## 18. Estimate roll-ups (resolves C-16, C-17, §5.6)

Trackers are arithmetically correct; **epics are wrong.** Each epic's headline estimate is corrected to
equal its tracker's verified sum:

| F | Correct (tracker) | Epic must say |
|---|---|---|
| F0 | 8.0 | 8.0 |
| F1 | 7.5 | 7.5 |
| F2 | 5.25 | 5.25 |
| F3 | 7.75 | 7.75 |
| F4 | recompute header | 10×M + 3×L ≈ 8.0 (13 rows) |
| F5 | 6.25 | 6.25 |
| F6 | 3.75 | 3.75 |
| F8 | 5.0 | 5.0 |
| F9 | 5.0 | 5.0 |
| F10 | 5.5 | 5.5 |

**Task count:** filesystem has **120** tasks (before the additions in §17). `docs/README.md` and F4
`index.md` currently say F4 = 11 (sums to 118); F4 has **13** rows → README total must read **120**.
After §17 additions the counts change; each agent updates its own feature count and the roll-up owner
(README) reconciles the grand total last.

---

## 19. Minor / low-severity fixes

- **D11 (C-24):** DECISION-LOG D11 claims "no bilingual `.uk.md` tier"; `DECISIONS-MATRIX.uk.md`
  exists. Correct D11 to acknowledge the Ukrainian decisions-matrix tier.
- **ADR-0005 O13 (C-25):** ADR-0005 cites `O13`; register defines O1–O12. Change the citation to the
  correct open decision (O5 — the tier/fallback decision) or drop the anchor.
- **D-27:** `infrastructure.md` §8 mis-cites `../engineering/deployment` as "caching applies to batch
  submissions"; `deployment.md` says nothing about caching. Remove/repoint the wikilink.
- **D-28:** `DECISIONS-MATRIX.uk.md` orders `D47a`/`D47b` (lines ~589/606) **before** `D47` (~621).
  Reorder so `D47` precedes `D47a`/`D47b`.
- **D-29:** `AddNpgsqlInstrumentation()` is not the real API — correct to the actual Npgsql
  OpenTelemetry surface (`.AddNpgsql()` on the tracer/meter builder).
- **D-30:** `b.UseIdentityAlwaysColumns()` comment claims it prevents implicit sequences on `uuid`
  keys; it concerns integer identity columns. Remove the method/comment for uuid keys.
- **Instrument count (C-30, §5.4-l):** F0 T11 says "seven domain instruments"; observability §2
  declares **eight**. Correct F0 T11 to **eight**.
- **F0 T13/T14 deps (C-29, §5.4-m):** tracker table and graph disagree. Canonical: **`T14 → T13`**
  (graph is right); fix the table so T13 deps include what the graph shows.
- **F10 `/cv` (C-26):** PRD §6.1 + SAD §2 say metadata-only; data-model §Handoffs says `/cv` uses F4's
  upload service. Canonical: **`/cv` is read-only metadata** (shows CV status/version); it does NOT
  upload. Correct the data-model §Handoffs line.
- **Invariant 7 negative test (§11-15):** add a test asserting no apply path exists (F6 test-plan).
- **F2 authorization AC (§4.2):** add an explicit authorization AC to F2 (the reprocess scope), so the
  DoD self-check is truthful.
- **Shared artifact-scanner harness (§11-16):** specify one shared scanner harness in
  `testing-strategy.md`, reused by G6 / F4 T10 / F6 T07 / F10 audit.

---

## 20. Parallel work-breakdown (resolves R-19, §11-17, §13-23)

The parallel implementation plan is [[AUDIT-RESOLUTION-TRACKER]]. It groups the 120+ tasks into
independent lanes keyed to feature ownership and the build-order graph, so multiple engineers/agents
can work concurrently without migration or file contention.
