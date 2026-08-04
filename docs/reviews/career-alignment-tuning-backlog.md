---
status: Backlog
owner: "Career-Alignment Audit"
reviewers: ["Viacheslav Melnichenko"]
updated_at: "2026-08-03"
tags: [review, career-alignment, backlog, jobhunter]
---

# Career-Alignment Tuning Backlog — JobHunter

> Concrete, actionable fine-tuning tasks derived from
> [[career-alignment-review|the career-alignment review]]. Each task names the target artifact, ties to
> a review finding, and carries a priority (P0/P1/P2) and rough size (S/M/L). These are **tuning /
> configuration / prompt changes**, not re-architecture — ADR-F4-0001 explicitly allows adding score
> components (`adr/0001-explainable-linear-scoring.md:92-94`) and weights are config, so the core fix
> is cheap.
>
> **Goal restated:** reliably surface and *top-rank* $180k–$400k+ remote AI-platform / platform /
> staff-engineering roles, and stop floating Senior-.NET/CRUD/enterprise roles to the top-10.

## Priority summary

| Theme | P0 | P1 | P2 |
|---|---|---|---|
| Scoring / ranking weights | TUNE-01, TUNE-02 | | |
| Enrichment signals | TUNE-03 | TUNE-04 | TUNE-11 |
| Owner-goal config | TUNE-05 | | |
| Anti-false-positive filters | TUNE-06 | | |
| Semantic vocabulary | | TUNE-07 | |
| Preference-learning guardrails | | TUNE-08 | |
| Discovery / company universe | | TUNE-09, TUNE-10 | |
| Title strategy | | | TUNE-12 |
| Recall protection / test gates | | | TUNE-13, TUNE-14 |

---

## Scoring / ranking weights

### TUNE-01 — Add an `alignment` score component  ·  P0 · M
- **Target:** `docs/features/f4-cv-matching-ranking/contracts/match-schema.md` §Ranking formula
  (`:149-164`); `ScoreCalculator` (F4 T07); `adr/0001-explainable-linear-scoring.md:54`.
- **Rationale (review §7, §1):** the current formula
  `100×(0.60·match+0.25·preference+0.15·freshness)×confidence` has no term that rewards AI-platform
  alignment, so fit-to-CV buries aspiration. Add a fifth, explainable component.
- **Proposed content:**
  `final = 100 × (0.45·match + 0.20·alignment + 0.20·preference + 0.15·freshness) × confidence`, where
  `alignment ∈ [0,1]` is a monotone function of `AiUsage` (None=0, Low=0.25, Medium=0.6, High=1.0) blended
  with role-family tier (Tier1=1.0, Tier2=0.7, Tier3=0.4, anti-goal=0.0). Persist it as a stored
  component like the others (QG-1 reconciliation still holds).

### TUNE-02 — Down-weight anti-goal roles in the score  ·  P0 · S  ·  ✅ Done (F4 T15)
- **Target:** `ScoreCalculator` / formula (`match-schema.md:155`); post-ranking suppression table
  (`match-schema.md:167-176`).
- **Rationale (review §8):** fit-dominant scoring promotes Senior-.NET/CRUD roles the Owner is leaving;
  nothing opposes this today.
- **Proposed content:** when `alignment` maps to anti-goal (AiUsage None/Low AND role-family CRUD/
  traditional-enterprise), apply a multiplicative penalty (e.g. `×0.5`) or, opt-in, a reason-logged
  suppression `"Anti-goal role family: {family}"` (invariant 11 — always retrievable, counted in footer).
- **Delivered (F4 T15):** `AntiGoalClassifier` classifies `AiUsage ∈ {None, Low, Unknown}` on
  `EnterpriseCrud` as anti-goal, with a family-naming reason. `ScoreComponents` gained a stored
  `AntiGoalMultiplier ∈ [0,1]` (reconcilable, QG-1) folded into the total like `confidence`.
  `Ranking:AntiGoalPenaltyFactor` (default 0.50) drives the down-weight; `Ranking:AntiGoalSuppression`
  (opt-in) turns it into a reason-logged suppression instead. The narrow predicate leaves the general
  non-target-family filter to TUNE-06/T17.

## Enrichment signals

### TUNE-03 — Emit a `RoleFamily` / title-tier classification  ·  P0 · M
- **Target:** F3 enrichment output `docs/features/f3-claude-batch-enrichment/contracts/enrichment-schema.md`
  (`:21-63`, prompt `:75-92`); alternatively a deterministic classifier in F2.
- **Rationale (review §3, §5, §7):** there is no structured notion of the Owner's Tier-1/2/3 target
  roles; `alignment` (TUNE-01) and F7 (TUNE-08) need a signal to act on.
- **Proposed content:** add `roleFamily` enum to `EnrichmentOutput`:
  `AiPlatform | Platform | AiApplications | ForwardDeployed | FoundingEng | BackendGeneric | Frontend |
  Fullstack | DevOpsSRE | MlResearch | DataScience | PromptEng | EnterpriseCrud | Other`, with a
  reason. Prompt guidance: classify by *the work described*, not the title string.

### TUNE-04 — Refine `AiUsage` resolution / add sub-signals  ·  P1 · M
- **Target:** `enrichment-schema.md:27,84-86`.
- **Rationale (review §3, §8):** a single 4-value scalar can't separate AI-platform engineering from
  ML-research from "uses Copilot." The target/trap boundary needs resolution.
- **Proposed content:** add booleans/enums such as `buildsAiProduct`, `buildsAiInfra`,
  `usesAiTooling`, `isResearch` alongside the existing scalar, each with the "engineering work" framing
  already in the prompt (`:84-86`).

### TUNE-11 — Sharpen the "AI company, CRUD work" guard into an acted-on signal  ·  P2 · S
- **Target:** `enrichment-schema.md:84-86` + `alignment` component (TUNE-01).
- **Rationale (review §8, MODERATE):** the AiUsage=Low definition is correct but inert because AiUsage
  isn't scored. Once TUNE-01 lands, ensure a low-AiUsage role at an AI-brand company scores by alignment,
  not by company prestige.

## Owner-goal configuration

### TUNE-05 — Encode the Owner's career goal in the Profile + match prompt  ·  P0 · M
- **Target:** `profiles` table `docs/features/f4-cv-matching-ranking/data-model.md:87-100`; match prompt
  `match-schema.md:60-103`.
- **Rationale (review §3, §5, §1):** the Profile holds present-facts only; the goal is encoded nowhere,
  and the match prompt is told fit ≠ desirability (`:69`). The system optimises the past.
- **Proposed content:** add Profile columns `target_role_families jsonb`, `desired_ai_usage_floor text`,
  optional `target_titles jsonb`. Add a match-prompt section: *"The candidate is deliberately targeting
  {target_role_families}. Reward genuine alignment to that trajectory even where it is a stretch;
  down-weight roles that would repeat their current track."* Keep CV handling rules intact — goal fields
  are Profile facts, not CV text, so no new leakage surface.
- **Delivered (F4 T16):** `Profile` gained `TargetRoleFamilies` (jsonb, deduped), `DesiredAiUsageFloor`
  (nullable enum-as-text; `Unknown` — the tolerant parser's sentinel — is rejected) and `TargetTitles`
  (jsonb, trimmed and deduped), added by migration `F4AddProfileCareerGoal` with a `[]` jsonb default so
  existing rows deserialize as empty. `MatchPrompt` renders the goal directive plus optional
  AI-usage-floor and title lines into the **candidate block** — before the cache breakpoint, since the
  goal is stable per Profile — **only when a goal is stated** (an unstated goal omits the section, same
  principle as the enrichment omission), so the shared-prefix guarantee holds. `PromptVersion` bumped to
  `match-v2` and the golden fixtures updated (G10). Integration tests round-trip the columns through real
  Postgres; the CV-leakage scan stays green because the fields are Profile facts, not CV text.

## Anti-false-positive filters

### TUNE-06 — Negative role-family list (ML-Researcher / Data-Scientist / Prompt-Engineer / CRUD)  ·  P0 · S
- **Target:** post-ranking suppression / down-weight (`match-schema.md:167-176`); uses `RoleFamily`
  from TUNE-03.
- **Rationale (review §8):** no explicit negative filter exists; research-adjacent or prompt-writing
  roles can slip through on fit + AiUsage.
- **Proposed content:** reason-logged down-weight (default) or opt-in suppression for
  `roleFamily ∈ {MlResearch, DataScience, PromptEng, EnterpriseCrud}` — reason
  `"Not a target role family: {family}"`, retrievable via `/hidden`, counted in the footer (invariant 11).
- **Delivered (F4 T17):** `NegativeFamilyClassifier` flags any `RoleFamily` in the configured negative
  set (`Ranking:NegativeRoleFamilies`, default `{MlResearch, DataScience, PromptEng}` — deliberately
  **disjoint** from T15's `EnterpriseCrud` anti-goal predicate, so the two never double-fire under
  defaults), with reason `"Not a target role family: {family}"`. `Ranking:NegativeFamilyPenaltyFactor`
  (default 0.50) drives the down-weight; `Ranking:NegativeFamilySuppression` (opt-in) turns it into a
  reason-logged suppression. The penalty folds into the same stored `AntiGoalMultiplier` career-policy
  slot as T15, so the total still reconciles from one slot (QG-1) — no new column, no migration.

## Semantic vocabulary

### TUNE-07 — Author and commit the F2 technology vocabulary with target-stack coverage  ·  P1 · M
- **Target:** the F2 T07 vocabulary YAML (`docs/features/f2-normalization-dedup/tasks/T07-technology-tagging.md:7,17`
  — file does not yet exist in repo).
- **Rationale (review §6, §3):** the "~300 technologies" YAML is unverifiable/absent; if it omits
  AI-native terms the deterministic tagger and F7 `Technology` dimension are blind to the target stack.
- **Proposed content — keywords to ADD (with aliases → canonical):** MCP / Model Context Protocol;
  Claude / Anthropic; OpenAI / GPT; Gemini; Cursor; Vercel AI SDK; Ollama; Bedrock; Azure OpenAI;
  Vertex AI; LangChain; LangGraph; Semantic Kernel; AutoGen; CrewAI; RAG; vector database (Pinecone,
  Weaviate, pgvector, Qdrant, Milvus, Chroma); embeddings; AI gateway; prompt management; LLM eval;
  guardrails; fine-tuning; inference serving; agent orchestration; tool/function calling; internal
  developer platform / IDP; platform engineering; Kubernetes; Docker; Terraform; Kafka; RabbitMQ;
  event-driven; CI/CD; GitOps; service mesh; Temporal; gRPC; Azure/AWS/GCP.

## Preference-learning guardrails

### TUNE-08 — Add `AiUsage` and `RoleFamily` as F7 preference dimensions  ·  P1 · M
- **Target:** F7 dimensions `docs/features/f7-preference-learning/data-model.md:109`; `Dimension` enum
  (F7 T01).
- **Rationale (review §3, §7):** current dimensions
  (`SalaryBand, Country, CompanySize, Technology, TimezoneBand, RemotePolicy, EmploymentType`) let the
  loop reinforce "more of what you clicked" but cannot reinforce the *trajectory*. Adding AiUsage /
  RoleFamily lets learning pull toward the target, under the existing ≥3-signal / ≤0.40-bound guards
  (`T04-weight-fitter.md:14-20`).
- **Proposed content:** extend the closed `Dimension` enum with `AiUsage` and `RoleFamily`; ensure
  `job_facts` snapshots them (`f7…/data-model.md:79-86`).

## Discovery / company universe

### TUNE-09 — Grow `companies.yaml` toward ~300 with pure-play AI/dev-tools/infra employers  ·  P1 · L
- **Target:** `tools/seed/companies.yaml:19-198` (currently 30 entries vs designed ~300,
  `adr/0001-company-registry-seeding.md:43`).
- **Rationale (review §3, §4):** coverage is the hard ceiling; the universe is under design scale and
  missing several top target employers.
- **Proposed content — candidates to add (verify ATS binding per entry):** OpenAI, Cursor/Anysphere,
  LangChain, Perplexity, Cohere, Hugging Face, Together AI, Replicate, Pinecone, Weaviate, Modal,
  Baseten, Temporal, Fly.io, Railway, Render, Grafana Labs, Confluent, dbt Labs, Retool, Zapier,
  Replit, Warp, Zed, Notion, Supabase, Neon, Render, Airbyte.

### TUNE-10 — Tag companies by comp band + remote-from-EMEA posture  ·  P1 · M
- **Target:** `companies.yaml` schema (`:8-14`) + discovery/digest prioritisation.
- **Rationale (review §4):** for a $180k–$400k remote-from-Kyiv goal the universe should be
  comp-and-remote-segmented; today it is a flat list mixing US high-comp with lower-band GB/EU firms.
- **Proposed content:** add optional `comp_band` and `remote_emea_friendly` fields; bias the digest
  toward the target band; feed `remote_emea_friendly` into acquisition prioritisation.

## Title strategy

### TUNE-12 — Encode Tier-1/2/3 target titles as a reference config  ·  P2 · S
- **Target:** new config consumed by TUNE-03 classifier and TUNE-05 Profile fields; F2 T02 currently
  extracts seniority only (`T02-title-normalization.md:5-11`).
- **Rationale (review §5):** the Owner's title tiers exist nowhere; a committed reference makes the
  classifier and scoring testable and reviewable in a diff.
- **Proposed content:** a `title-tiers.yaml` mapping the Tier-1/2/3 title lists from the review §5 to
  role-family archetypes.

## Recall protection & test gates

### TUNE-13 — Soften the seniority-floor pre-match rule for early-stage/founding roles  ·  P2 · S
- **Target:** pre-match filter `docs/features/f4-cv-matching-ranking/adr/0003-pre-match-filter-and-cv-caching.md:68`;
  F4 T12.
- **Rationale (review §9, MODERATE):** "two or more levels below" plus erratic startup levelling risks
  dropping Founding-Engineer / early-startup roles the Owner wants.
- **Proposed content:** exempt `CompanyStage ∈ {Seed, SeriesA}` from the seniority-floor exclusion, or
  require an explicit parsed down-level rather than an absolute two-level gap.
- **Delivered (F4 T18):** `PreMatchFilter` exempts any role whose enrichment `CompanyStage` is in the
  configured `PreMatch:SeniorityFloorExemptStages` (default `{Seed, SeriesA}`) from the seniority floor
  only — every other factual rule still applies. The exemption is evidence-driven (a job with no
  enrichment stage cannot claim it) and turns off with an empty set, restoring the pre-T18 behaviour.

### TUNE-14 — Add a target-role-family slice to the golden ranking set  ·  P2 · M
- **Target:** golden ranking set `docs/features/f4-cv-matching-ranking/tasks/T11-golden-ranking.md`.
- **Rationale (review §10.11):** alignment regressions must fail the build. Precision@10 is the KPI
  (`f4…/PRD.md:167`); add cases asserting a stretch Tier-1 role out-ranks a perfect-fit CRUD role.
- **Proposed content:** ≥10 golden cases pairing a target-family role against a high-fit anti-goal role,
  asserting relative order (bands, not exact scores) so TUNE-01/02/05 are gated by the build (G10).
- **Delivered (F4 T19):** `tests/…/Data/golden-target-family-slice.yaml` + `GoldenTargetFamilySliceTests`.
  Ten pairs, each coupling a stretch Tier-1 target role against a higher-raw-fit off-target role — five
  against anti-goal enterprise-CRUD (T15), five against the off-target family set (T17). Judged by the same
  pure chain as the golden set, with a deliberately neutral pre-match (every role Senior / FullTime /
  EMEA-remote / enriched / just-seen), so the slice isolates alignment + career-policy. Asserts, per pair:
  the off-target is the stronger raw fit (or the test proves nothing), both sides land their recorded band,
  and the target lands a strictly better band *and* a higher final score. Fails the build the moment a
  re-weighting lets a high-fit off-target role out-rank a stretch Tier-1 role.
