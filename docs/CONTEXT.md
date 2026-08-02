---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "00"
ticket: ""
tags: [context, glossary, invariants, jobhunter]
---

# CONTEXT — JobHunter domain

> **The canonical vocabulary.** Every other document, every class name, every table name,
> every event name uses these terms and only these terms. If a word is not here, it does not
> exist in the domain. If two words mean the same thing, one of them is wrong.

Source of the original idea: [`AI_Job_Intelligence_Platform_Plan.md`](../AI_Job_Intelligence_Platform_Plan.md).
Expanded rationale: [[00-overview/idea-brief|Idea brief]].

---

## 1. Glossary

| Term | Definition | NOT this |
|------|------------|----------|
| **Owner** | The single human the platform serves. Identified by one Telegram chat id and one Keycloak subject. | Not a "user" — there is no multi-tenancy, no sign-up, no roles beyond Owner. |
| **Profile** | The Owner's structured career facts: seniority, stacks, salary floor, timezone band, contract preferences, countries. Derived from the CV plus explicit overrides. | Not the CV document itself. |
| **CV** | One uploaded document (PDF/Markdown) plus its extracted plain text. Versioned; exactly one version is `active` at a time. | Not the Profile. A CV *feeds* a Profile. |
| **Company** | A hiring organisation, uniquely keyed by canonical domain (`stripe.com`). Carries an ATS binding once detected. | Not a job board. Not a recruiting agency listing. |
| **ATS** | Applicant Tracking System hosting a Company's public job feed — Greenhouse, Lever, Ashby, Workable, SmartRecruiters, Recruitee. | Not an aggregator (Indeed, LinkedIn). Aggregators are explicitly out of scope. |
| **ATS Binding** | The `(Company, AtsKind, BoardToken)` triple that makes a Company's feed fetchable, plus the confidence and evidence for that detection. | Not a URL alone. |
| **Source** | A concrete fetchable endpoint: an ATS board, an RSS feed, a company careers page. Implements one port, `IJobSource`. | Not a Company. One Company may expose several Sources. |
| **RawPosting** | The verbatim payload fetched from a Source, stored immutably with its fetch metadata and content hash. Never edited, only superseded. | Not a Job. |
| **Job** | The normalised, deduplicated canonical vacancy: title, company, locations, remote policy, employment type, description, apply URL, posted/closed timestamps. | Not the RawPosting, not the ranking. |
| **Fingerprint** | The deterministic dedup key of a Job: `sha256(canonicalCompanyDomain ‖ normalisedTitle ‖ normalisedLocationSet)`. Two Jobs with one Fingerprint are one Job. | Not the content hash of a RawPosting. |
| **Enrichment** | The Stage-2 Claude output attached to a Job: salary estimate, remote/contractor flags, timezone band, AI-usage level, technologies, company stage, reasons. Company-agnostic — it describes the *job*, not the fit. | Not the Match. |
| **Match** | The Stage-3 Claude output for a `(Job, Profile)` pair: match score 0–100, missing skills, interview probability, salary expectation, reasons. | Not the Enrichment. |
| **Score** | The final ranking number the Owner sees. `Score = f(Match.score, Preference weights, freshness)`. Computed by us, not by Claude. | Not `Match.score` verbatim. |
| **Run** | One end-to-end execution of the daily pipeline, from discovery cut-off to delivered Digest. Has an id, a state machine, a cost ledger, and is resumable. Its state machine has nine values: `Created, Enriching, Matching, Ranking, Researching, Reporting, Delivered, Failed, CostAborted` (there is no `Discovering` state). | Not a Batch. One Run submits several Batches. |
| **Batch** | One Anthropic Message Batches API submission and its lifecycle (`submitted → in_progress → ended`). Belongs to exactly one Run and one Stage. | Not an individual model call. |
| **ModelTier** | `Cheap` (triage/extraction) or `Deep` (matching/synthesis). Selects model id and per-token price. | Not a model name — the mapping tier→model lives in configuration. |
| **Digest** | The single generated morning report for one Run: counts, statistics, market note, and the ordered list of Cards. | Not a Telegram message. A Digest *renders into* Telegram messages. |
| **Card** | One Job inside a Digest, rendered with its Score, reasons, and the four actions: Open, Ignore, Save, Applied. | Not a Job. A Card is a presentation of a Job in a Digest. |
| **Application** | The Owner's pipeline record for one Job: status, timeline of transitions, notes, reminders. | Not the Job. |
| **ApplicationStatus** | `New → Saved → Applied → Interview → Rejected \| Offer`, plus `Ignored` (terminal, load-bearing preference evidence). Seven states; the legal-transition table is a 7×7 = 49-pair matrix including self-transitions. | Not free text. |
| **Signal** | One recorded Owner action carrying preference information (`Opened`, `Ignored`, `Saved`, `Applied`, `Interview`, `Offer`, `Rejected`, `Rated`) with the Job facts at the moment of the action. | Not a Match. Signals are *evidence*; the Preference model is the *conclusion*. |
| **PreferenceModel** | The learned weights derived from Signals: salary floor, country weights, company-size weights, technology weights, timezone weights. Versioned and explainable. | Not a black box. Every weight must cite the Signals that produced it. |
| **CompanyResearch** | The agent-generated dossier for a Company: funding, engineering blog, open source, reviews, recent news, layoffs, stack, interview process — each item citing a Source URL. | Not the Company record. |
| **CostLedger** | Per-Run accumulation of token usage and USD cost per Stage and ModelTier, checked against a hard ceiling. | Not a bill. |

---

## 2. Pipeline stages

The canonical stage names. These appear verbatim as event names, queue names, metric labels and
Run state values.

| # | Stage | Input | Output | Cadence |
|---|-------|-------|--------|---------|
| 1 | `Discovery` | Company registry | `RawPosting` | every 6 h |
| 2 | `Normalization` | `RawPosting` | `Job` (candidate) | on event |
| 3 | `Deduplication` | `Job` (candidate) | `Job` (canonical) + `Fingerprint` | on event |
| 4 | `Enrichment` | new `Job`s of the day | `Enrichment` | daily, batched |
| 5 | `Matching` | `Job` + `Enrichment` + `Profile` | `Match` | daily, batched |
| 6 | `Ranking` | `Match` + `PreferenceModel` | `Score` | daily |
| 7 | `Research` | top-N Companies | `CompanyResearch` | daily, batched |
| 8 | `Reporting` | Scored Jobs | `Digest` | daily |
| 9 | `Delivery` | `Digest` | Telegram messages | daily 07:00 Europe/Kyiv |

---

## 3. Invariants

These hold at all times. A violation is a defect, not a feature request.

1. **A RawPosting is immutable.** Re-fetching produces a new RawPosting row; it never updates an old one.
2. **One Fingerprint, one Job.** Deduplication merges into the earliest-seen Job and records the alias.
3. **Every Enrichment and every Match belongs to exactly one Job and one Run.** Re-running a Stage supersedes by `(job_id, run_id, stage)`, never duplicates.
4. **Every Score is explainable.** A Card without at least one reason string is invalid and must not be delivered.
5. **Every CompanyResearch claim cites a URL.** An uncited claim is dropped, not shown.
6. **A Run never exceeds its cost ceiling.** The `CostLedger` is checked *before* each Batch submission; exceeding the ceiling aborts the Stage and marks the Run `CostAborted`, it does not silently truncate.
7. **The platform never applies to a job.** It never submits a form, never sends an email to a recruiter, never impersonates the Owner. `Applied` is a status the Owner sets, not an action the system performs.
8. **Delivery is idempotent.** A `(run_id, chat_id, card_key)` triple is delivered at most once, enforced by a unique delivery-log row.
9. **Single Owner.** No registration, no tenant column, no role model. Authorisation is a Keycloak subject allowlist for the API and a Telegram chat-id allowlist for the bot. The Owner is the sole principal; `CommandCapability { Standard, Sensitive }` is a per-command sensitivity flag (an extra confirmation on destructive commands), **not** a second role or identity.
10. **Robots and rate limits are respected.** Every Source declares a rate budget; the fetcher honours `robots.txt`, `Retry-After` and a per-host token bucket. A Source that returns 403/429 twice in a row is quarantined, not retried harder.
11. **Preference learning never hard-filters silently.** A learned filter suppresses a Job only with a recorded reason, and the Digest always reports how many Jobs were suppressed and why.
12. **Secrets never enter the repository, the image layers, or the logs.**

---

## 4. Explicitly out of scope

Recording these prevents re-litigation.

- **LinkedIn scraping.** Anti-bot posture, ToS exposure, and brittleness outweigh the marginal listings. Revisit only via an official API. See [[DECISION-LOG|D3]].
- **Aggregator boards** (Indeed, Glassdoor listings, Google Jobs). Duplicated, stale, and hostile to automation.
- **Auto-apply / auto-outreach.** Invariant 7.
- **Multi-user SaaS.** No tenancy, no billing, no onboarding funnel. The architecture must not *forbid* it later, but nothing is built for it now.
- **Mobile app.** Telegram is the client.
- **Résumé rewriting / cover-letter generation.** Post-MVP candidate, tracked in [[BACKLOG]].
- **Recruiter-side features.** This is a candidate tool.

---

## 5. Quality goals (ranked)

1. **Signal over volume.** A day with 6 accurate Cards beats a day with 40 mediocre ones. Precision at the top of the ranking is the product.
2. **Resumability.** Any Stage can crash at any point; re-running the Run must converge to the same result without duplicate cost or duplicate delivery.
3. **Cost predictability.** Daily LLM spend is bounded and observable to the cent.
4. **Explainability.** Every number shown to the Owner traces back to a stored input.
5. **Showcase quality.** The repository is a portfolio artifact: the architecture, the tests and the docs are part of the deliverable, not overhead.

---

## Related

- [[00-overview/idea-brief|Idea brief]] — why this project, which approach, what was rejected
- [[00-overview/sad|System Architecture Document]] — how it is built
- [[BACKLOG]] — what is next
- [[DECISION-LOG]] — cross-cutting decisions D1…Dn
- [[architecture/event-catalog|Event catalog]] — the wire contracts
