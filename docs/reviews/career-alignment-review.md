---
status: Review
owner: "Career-Alignment Audit"
reviewers: ["Viacheslav Melnichenko"]
updated_at: "2026-08-03"
tags: [review, career-alignment, jobhunter]
---

# Career-Alignment Review — JobHunter

> **Lens:** Is the platform *configured* to reliably surface $180k–$400k+ **remote AI-platform /
> platform / staff-engineering** roles for the Owner — and to avoid steering the Owner back into the
> Senior-.NET / CRUD / enterprise track they are trying to leave?
>
> **Method:** read-only audit of the data and design that constitute the platform's "career
> configuration": the seed company universe, the title/discovery strategy, the F2 technology
> vocabulary, the F3 enrichment schema, the F4 match prompt + scoring formula, the F4 Profile, and the
> F7 preference-learning dimensions. Every substantive claim cites a file. CV content is never quoted;
> alignment is assessed abstractly.
>
> **Implementation caveat (load-bearing):** only **F0 (foundation)** and **F1 (discovery)** exist in
> `src/` — the source tree contains `Domain/Companies`, `Domain/Postings`, `Domain/Sources` and the
> scraper adapters, and nothing for normalization, enrichment, matching, ranking or preferences. **F2,
> F3, F4, F5 and F7 are docs-only drafts** (`status: Draft` on every PRD/contract read). The
> `TechnologyTagger` vocabulary YAML that F2 T07 promises (`tools/seed/…` equivalent) **does not exist
> in the main repo**. Therefore most of the "career configuration" below is *designed, not yet
> observable behaviour*, and an unimplemented matcher cannot be trusted until it is built and its golden
> set is green. Findings distinguish "present in repo" from "designed but absent".

---

## 1. Overall assessment

The platform's **acquisition layer is well-aligned** and the **scoring layer is structurally
mis-aligned with the Owner's stated goal** — a subtle but decisive combination.

Discovery is *company-board-based*, not title-query-based: F1 fetches every open role from each
company's Greenhouse/Lever/Ashby/Workable board
(`docs/features/f1-ats-job-discovery/index.md:16-18`, tasks
`T05-ijobsource-greenhouse.md`, `T06-lever-ashby-workable.md`). This is the single best design choice
for the Owner's goal, because it means an excellent *generic-titled* role ("Senior Software Engineer"
at an AI-infra company) is never filtered out at the door by a title keyword. The seed universe
(`tools/seed/companies.yaml:19-198`) is heavily biased toward exactly the right employers: Anthropic,
Mistral AI, Databricks, HashiCorp, Sourcegraph, Vercel, Cloudflare, Elastic, Linear, PostHog, GitLab,
Figma, plus a fintech cohort. For an AI-platform career, this is a strong, deliberately curated core.

The problem is what happens *after* acquisition. The ranking that decides the 07:00 digest is
`final_score = 100 × (0.60·match + 0.25·preference + 0.15·freshness) × confidence`
(`docs/features/f4-cv-matching-ranking/contracts/match-schema.md:155`,
`adr/0001-explainable-linear-scoring.md:54`). **Fit dominates at 0.60, and "fit" is explicitly
defined as fit to the Owner's *current* CV, not alignment to the Owner's *target* trajectory.** The
match prompt states: *"matchScore is fit, not desirability… Weight what they have actually done far
above what they list as a skill"* (`match-schema.md:69,67-68`). Nowhere in the Profile
(`docs/features/f4-cv-matching-ranking/data-model.md:91-100`), the match prompt, or the score formula
is the Owner's **career goal / target role family** encoded. The consequence: because the CV
describes a strong senior backend/.NET background, the system will **structurally over-rank the very
roles the Owner is trying to escape** (Senior .NET, backend CRUD at a great company) and has no
first-class mechanism to *reward* an AI-platform role that is a slight stretch. The one aligned
signal — F3's `AiUsage` enrichment (`enrichment-schema.md:27,84-86`) — is fed to the match prompt as
context but is **not a weighted term in the scoring formula and not a preference-learning dimension**.

Net: the platform will find the right *companies* and surface the right roles *somewhere* in the list,
but its ordering optimises "roles you can already get" over "roles that advance you," which is the
opposite of the stated objective. This is fixable with configuration and prompt/formula changes (see
§10 and the tuning backlog), and none of it requires abandoning the architecture.

## 2. Strengths

- **Company-board discovery avoids the title-filter trap.** Whole boards are fetched
  (`docs/features/f1-ats-job-discovery/index.md:16-18`; adapters in `tasks/T05`, `T06`,
  `T07-jsonld-careers-adapter.md`). There is *no* title allow/deny list anywhere in F1 — verified by
  reading every F1 task and the seed schema. A weak-titled but excellent role is not dropped at
  acquisition. This directly serves false-negative avoidance (§9).
- **The curated company universe is AI-platform-biased by design.**
  `tools/seed/companies.yaml:19-198` includes Anthropic, Mistral AI, Databricks, HashiCorp,
  Sourcegraph, Vercel, Cloudflare, Elastic, Linear, PostHog, GitLab, Netlify, Figma. ADR-F1-0001
  makes curation an explicit editorial act (`adr/0001-company-registry-seeding.md:28-44`) — "companies
  worth working for is a judgement, not a query." This is the right posture for a precision-first tool.
- **`AiUsage` is the correct anti-false-positive primitive, and its definition is excellent.** The
  enrichment prompt says *"AI usage is how much the ENGINEERING work involves building with or on AI
  systems. A company that sells an AI product but whose posting describes CRUD work is Low"*
  (`enrichment-schema.md:84-86`). This is exactly the distinction that separates a real AI-platform
  role from an "AI company doing CRUD" trap. The signal exists and is well-specified — it is simply
  under-used downstream.
- **Scoring is explainable, deterministic and tunable without a deploy or a model change**
  (`adr/0001-explainable-linear-scoring.md:78-95`). Weights are configuration. This means the
  alignment fixes in §10 are *cheap*: re-weighting or adding a component is a config/prompt change, not
  a re-architecture.
- **Precision-first quality goal** (`docs/CONTEXT.md:107`) matches a career search where a day with 6
  right cards beats 40 mediocre ones.
- **F7's evidence floor and bounding are sane guardrails** — ≥3 signals per weight, no dimension >0.40
  of the component, indifferent profile produces no weights
  (`docs/features/f7-preference-learning/tasks/T04-weight-fitter.md:14-20`). Learning cannot run away.
- **Freshness weight is modest (0.15) and decays gracefully** (`match-schema.md:162-164`) — a
  fortnight-old excellent role still shows. Good for a market where great roles are not always fresh.

## 3. Weaknesses

- **The Owner's career *goal* is encoded nowhere.** The Profile holds seniority, salary floor,
  timezone, countries and employment types (`f4…/data-model.md:91-100`) — all facts about the present,
  none about the *target*. There is no `target_titles`, no `target_role_families`, no
  `desired_ai_usage_floor`, no "trajectory" field. The system optimises fit-to-CV, and the CV is the
  past.
- **Fit dominates ordering (0.60) and fit is defined against the current CV, not the target.**
  `match-schema.md:69` ("fit, not desirability") + formula `:155`. A Senior .NET/CRUD role at a good
  company will score *higher* than a stretch AI-platform role, which is precisely backwards for a
  career-change objective.
- **`AiUsage` is not a scoring term and not a preference dimension.** It enters the match prompt as one
  context line (`match-schema.md:99`) and is otherwise inert. The formula (`:155`) has no AI/alignment
  component; the F7 dimensions are `SalaryBand, Country, CompanySize, Technology, TimezoneBand,
  RemotePolicy, EmploymentType` (`f7…/data-model.md:109`) — **no `AiUsage`, no role-family**. The one
  signal that distinguishes target from trap cannot influence the order except implicitly through the
  model's own weighting inside `matchScore`.
- **The technology vocabulary is unverifiable and its target-stack coverage is unspecified.** F2 T07
  promises "~300 canonical technologies" in a committed YAML (`f2…/T07-technology-tagging.md:7,17`) but
  **that file does not exist in the main repo** (F2 is docs-only). The task text names no AI-native
  terms (MCP, Claude, Anthropic, OpenAI, LangGraph, Semantic Kernel, RAG, vector DB, AI gateway, IDP,
  agent orchestration). Whether the Owner's target stack is even taggable is currently unknown — and if
  it is missing, the deterministic `Technology` dimension F7 learns on cannot reinforce AI-native
  enthusiasm.
- **Title normalization does no role-family classification.** F2 T02 extracts *seniority* only and
  canonicalises abbreviations (`T02-title-normalization.md:5-11`); it explicitly defers "model-based
  title understanding" to F3. There is no tiering of titles into the Owner's Tier-1/2/3 target scheme,
  so the platform has no structured notion of "this is an AI Platform Engineer title."
- **The seniority-floor pre-match filter can silently drop stretch/founding roles.** ADR-F4-0003 excludes
  jobs "two or more levels below the Profile's seniority"
  (`adr/0003-pre-match-filter-and-cv-caching.md:68`). Founding/early-startup titles are erratically
  levelled; a mis-parsed "Founding Engineer" or an unlevelled startup post risks exclusion. It is
  reason-logged and retrievable (good), but it is still off the digest.
- **`AiUsage` scale is coarse.** A single 4-value scalar (`None|Low|Medium|High`,
  `enrichment-schema.md:27`) cannot distinguish "AI-platform/infra engineering" from "ML research" from
  "uses Copilot daily." The target/trap boundary needs finer resolution than one enum.
- **Seed universe is tiny and comp-mixed vs the design.** ADR-F1-0001 says "~300 curated companies"
  (`adr/0001-company-registry-seeding.md:43`) but `companies.yaml` holds **30**
  (`companies.yaml:19-198`). Several (Monzo, Wise, Gymshark, Mistral-FR) sit in comp bands well below
  the $180k+ target for a remote EMEA hire. Not a defect, but the universe is neither at design scale
  nor comp-screened for the target band.
- **Everything downstream of discovery is unimplemented.** F2–F7 are `Draft` docs; no matcher, scorer
  or vocabulary runs today. The alignment verdict is therefore about *design intent*, and design intent
  currently mis-weights.

## 4. Missing search strategies

- **No target-role-family targeting at any layer.** Discovery is company-based (good), but there is no
  compensating *role-family scoring* to promote the target roles once fetched. The strategy relies
  entirely on the LLM match prompt inferring desirability from the CV, which it is explicitly told *not*
  to do (`match-schema.md:69`).
- **No comp-band screening of the company universe.** For a $180k–$400k remote goal, the seed should be
  tagged/segmented by plausible comp band and remote-hiring posture; today it is a flat list
  (`companies.yaml`).
- **No "remote-for-EMEA / AMER-overlap-acceptable" acquisition strategy.** The Owner is Europe/Kyiv;
  the highest-comp roles are US companies. Discovery does not prioritise companies known to hire
  remote-from-EMEA. The timezone logic only bites *later* as a factual pre-match exclusion
  (`adr/0003…:68`), not as an acquisition-prioritisation strategy.
- **No directory-crawl bias toward AI-native employers.** ADR-F1-0001's weekly crawl
  (`adr/0001-company-registry-seeding.md:44-51`) proposes companies generically; there is no signal
  that it should preferentially propose AI-platform / dev-tools / infra companies.
- **No "second-pass" for role-family on generic-titled roles.** F3 could emit a role-archetype signal;
  it does not. So a "Senior Software Engineer" that is really platform/agent work is only recoverable
  via free-text `technologies` + `AiUsage`, both under-weighted.

## 5. Missing job titles

The Owner's Tier-1/2/3 target titles are **not encoded anywhere** — not in the Profile, not in a
title-tier config, not in the match prompt. Because discovery is board-based this does not block
acquisition, but it means nothing *rewards* these titles. The tuning backlog proposes encoding them.
Titles/role-families with no representation in any config today:

- **Tier 1:** AI Platform Engineer, AI Systems Engineer, AI Solutions Engineer, Forward Deployed
  Engineer, Founding Engineer (AI).
- **Tier 2:** Platform Engineer, Senior Platform Engineer, AI Infrastructure Engineer, AI Applications
  Engineer, Applied AI Engineer, AI Integration/Enablement Engineer, Staff Software/Backend Engineer
  (AI), AI Product Engineer.
- **Tier 3 (judge by description):** Senior Software Engineer, Backend Engineer, Technical Lead,
  Solutions/Platform/Technical Architect.

The only title processing that exists is seniority extraction (`f2…/T02-title-normalization.md:5-11`).

## 6. Missing semantic keywords

The deterministic vocabulary (F2 T07) is unverifiable (file absent) and its task text names no
AI-native terms. Whether present in the model's implicit knowledge (F3) or absent from the
deterministic tagger, the following target-stack keywords have **no confirmed representation** and
should be explicitly added to the F2 vocabulary so the F7 `Technology` dimension can learn on them:

- **Agent / orchestration:** MCP (Model Context Protocol), agent orchestration, tool calling,
  function calling, LangGraph, LangChain, Semantic Kernel, AutoGen, CrewAI, agentic workflows.
- **LLM providers / SDKs:** Claude, Anthropic, OpenAI, GPT, Gemini, Cursor, AI SDK (Vercel),
  Ollama, Bedrock, Azure OpenAI, Vertex AI.
- **AI-platform patterns:** RAG, vector database (Pinecone, Weaviate, pgvector, Qdrant, Milvus),
  embeddings, AI gateway, prompt management, LLM eval/evaluation, guardrails, observability for LLMs,
  fine-tuning ops, inference serving.
- **Platform / infra:** internal developer platform (IDP), platform engineering, Kubernetes, Docker,
  Terraform, event-driven, message queue (Kafka, RabbitMQ), CI/CD, GitOps, service mesh, Azure/AWS/GCP.
- **Enterprise-AI adoption:** AI enablement, developer productivity, AI-assisted SDLC, rapid PoC,
  enterprise AI integration.

## 7. Missing scoring signals

- **No AI-usage / career-alignment term in the score formula.** The formula
  (`match-schema.md:155`) has match, preference, freshness — no term that rewards high `AiUsage` or a
  target role-family. This is the single most consequential gap.
- **No penalty for anti-goal roles.** Nothing down-weights pure CRUD/traditional-enterprise, ML-research
  or prompt-writing roles. Fit-dominant scoring will *promote* a well-matched CRUD role.
- **No stretch/aspiration reward.** A career-change tool should give a controlled boost to slightly-stretch
  target roles; the formula cannot express interactions and has no aspiration term (the ADR itself notes
  the linear model cannot express "high AI usage matters only at senior level",
  `adr/0001-explainable-linear-scoring.md:87-88`).
- **No role-family / title-tier component.** Tier-1 target titles get no score bonus.
- **`preference_component` cannot carry AI-usage or role-family** because those are not F7 dimensions
  (`f7…/data-model.md:109`).

## 8. False-positive risks

- **HIGH — Senior .NET / backend CRUD at great companies.** This is the dominant risk. Fit-to-CV
  scoring (`match-schema.md:69`, formula `:155`) will rank these highly *precisely because* the CV
  matches them, and the excellent company universe guarantees they exist in the feed. The platform will
  cheerfully recommend the roles the Owner is trying to leave. **No filter or down-weight opposes this.**
- **LOW–MODERATE — ML Researcher / Data Scientist.** Partially mitigated: an engineering CV yields low
  fit for research roles, and the `AiUsage` definition (`enrichment-schema.md:84-86`) is about
  *engineering* work with AI, not research. But there is *no explicit negative filter*, so a
  research-adjacent "ML Platform Engineer" could slip through on fit + high AiUsage.
- **MODERATE — "AI company, CRUD work."** The `AiUsage=Low` definition is the right guard
  (`enrichment-schema.md:84-86`), *but* since AiUsage isn't a scoring term, a low-AiUsage CRUD role at
  Anthropic/Databricks still scores on pure fit. The guard exists in data but doesn't act on the order.
- **LOW — Prompt Engineer.** Low CV fit likely suppresses these, but again no explicit suppression rule.

## 9. False-negative risks

- **MODERATE — excellent generic-titled roles ranked too low.** The company-board strategy *fetches*
  them (strength, §2), but fit-dominant scoring may rank a stretch "Senior SWE (platform)" *below* a
  perfect-fit CRUD role. They appear in the digest but possibly below the top-10 the Owner reads. The
  KPI is precision@10 (`f4…/PRD.md:167`), so being pushed past rank 10 is effectively a miss.
- **MODERATE — seniority-floor pre-match exclusion of founding/startup roles.** "Two levels below"
  exclusion (`adr/0003…:68`) plus erratic startup levelling risks dropping Founding-Engineer-type roles
  the Owner would want. Retrievable via `/hidden` but off the digest.
- **LOW–MODERATE — timezone exclusion of onsite-leaning US roles.** The pre-match rule only excludes
  when *timezone-incompatible AND not remote* (`adr/0003…:68`), so genuinely remote AMER roles survive
  — good. But a remote-but-PST-overlap role tagged AMER for an EMEA owner is a practical stretch that
  the system will still show; that is the safe direction, so low risk.
- **LOW — vocabulary miss hiding technology enthusiasm.** If the F2 vocabulary omits AI-native terms
  (unverifiable, §6), the F7 `Technology` dimension never learns the Owner's AI enthusiasm, so
  preference can't compensate for weak fit. Compounds the §7 gap.

## 10. Recommendations (concrete, prioritized)

**P0 — encode the goal and let it move the order (the core fix):**
1. Add a **fifth score component, `alignment`**, derived from `AiUsage` + a role-family classification,
   e.g. `final = 100 × (0.45·match + 0.20·alignment + 0.20·preference + 0.15·freshness) × confidence`.
   Fit stays important but no longer buries aspiration. (Config/formula only — ADR-F4-0001 already
   anticipates adding components, `:92-94`.)
2. Add a **role-family / title-tier signal** to F3 enrichment (or a deterministic classifier in F2),
   emitting the Owner's Tier-1/2/3 archetype, so `alignment` and F7 have something structured to act on.
3. Add **Owner-goal fields to the Profile** (`target_role_families`, `desired_ai_usage_floor`,
   optional `target_titles`) and **feed the goal into the match prompt** so `matchScore`/reasons weigh
   *desirability toward the target*, not only fit-to-past.

**P0 — anti-false-positive guardrails:**
4. Add an **explicit down-weight (or opt-in suppression) for anti-goal roles**: `AiUsage=None/Low` AND
   role-family in {CRUD/traditional-enterprise}, and a negative list for ML-Researcher / Data-Scientist /
   Prompt-Engineer archetypes. Reason-logged per invariant 11.

**P1 — semantic coverage:**
5. **Author and commit the F2 technology vocabulary YAML** and ensure it contains the §6 keyword set
   (MCP, Claude/Anthropic/OpenAI, LangGraph/Semantic Kernel/AutoGen/CrewAI, RAG/vector-DB, AI gateway,
   IDP, K8s, event-driven, etc.). Without this the deterministic tagger and F7 `Technology` dimension
   are blind to the target stack.
6. **Add `AiUsage` and `RoleFamily` as F7 preference dimensions** so the learning loop reinforces the
   *trajectory*, not just "more of what you clicked" (`f7…/data-model.md:109`).

**P1 — discovery/company universe:**
7. **Grow `companies.yaml` toward the designed ~300** and add pure-play AI/dev-tools/infra employers
   currently absent (e.g. OpenAI, Cursor/Anysphere, LangChain, Perplexity, Cohere, Hugging Face,
   Together, Replicate, Pinecone, Weaviate, Modal, Baseten, Temporal, Fly.io, Railway, Render, Grafana,
   Confluent, dbt Labs, Retool, Zapier, Replit, Warp, Zed, Notion).
8. **Tag companies by plausible comp band and remote-from-EMEA posture** and bias the digest/discovery
   toward the target band.

**P2 — recall protection & finer resolution:**
9. **Soften the seniority-floor pre-match rule for early-stage/founding titles** (e.g. exempt Seed/SeriesA
   companies, or require an explicit down-level rather than absolute two-level).
10. **Refine `AiUsage`** to distinguish AI-platform/infra engineering from ML-research from
    tooling-consumer, so the target/trap boundary has resolution.
11. **Add a precision@10 slice for target-role-family** to the golden ranking set
    (`f4…/T11-golden-ranking.md`) so alignment regressions are caught by the build.

## 11. Final confidence score

**58 / 100.**

Rationale: the acquisition foundation is genuinely well-aligned (company-board discovery, AI-biased
curated universe, an excellent `AiUsage` definition) and the architecture makes every needed fix cheap
(explainable, config-tunable scoring). But the platform as *currently configured* optimises
fit-to-current-CV over advancement-to-target, encodes the Owner's goal nowhere, leaves the one aligned
signal (`AiUsage`) out of both the score formula and the learning loop, and cannot verify that its
technology vocabulary even covers the target stack. On top of that, everything past discovery is
unimplemented draft. The score reflects strong bones and a correctable-but-real mis-aim, not a finished,
trustworthy matcher.

---

## Final verdict

*"If I were personally using this platform to find a $200k+ AI Platform Engineering career, would I
trust it?"*

**YES WITH IMPROVEMENTS.**

I would trust it to **find the right companies and put the right roles somewhere in the feed** — the
board-based discovery over an AI-biased curated universe is exactly right, and it will not silently
drop a great weak-titled role at acquisition. I would **not yet trust its ordering**, because the
fit-dominant score (0.60, defined as fit-to-current-CV and explicitly *not* desirability,
`match-schema.md:69,155`) will structurally float Senior-.NET/CRUD roles to the top-10 the Owner
actually reads, while the one signal that separates a real AI-platform role from a trap — `AiUsage`
(`enrichment-schema.md:84-86`) — never touches the order or the learning loop. The Owner's career goal
is encoded in no artifact I could find (Profile has present-facts only, `f4…/data-model.md:91-100`).
Add an `alignment` score component driven by `AiUsage` + role-family, encode the target role-families
in the Profile and match prompt, add anti-goal down-weights, commit a target-stack technology
vocabulary, and give F7 `AiUsage`/`RoleFamily` dimensions — with those (all cheap, all config/prompt
level) this becomes a **YES**. Until then, and until F2–F7 are actually implemented and their golden
sets are green, it is a promising, well-architected tool aimed slightly but decisively at the wrong
target.
