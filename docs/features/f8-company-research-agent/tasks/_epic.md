---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f8-company-research-agent, mvp, jobhunter]
---

# Epic — F8 Company Research Agent

Automatically produce a concise dossier for companies that reach the top of the digest — funding,
engineering blog, open source, reviews, news, layoffs, stack and interview process — **with a URL
beside every claim**.

The citation guarantee is the feature. A dossier of plausible unsourced statements is worse than no
dossier, because the Owner might act on it. So the architecture inverts the usual order: fetch first,
store every document with its URL, synthesise only over what was fetched, and discard any claim whose
citation is not in the set.

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-07, AC-01…AC-10
- SAD: [[../sad|sad]] — fetch-then-synthesise, citation verification, safe fetching
- Data model: [[../data-model|data-model]] — `company_research`, `research_sources`, `research_claims`
- Contract: [[../contracts/research-schema|research schema]] — schema, prompt, citation rules, fetchers
- Test plan: [[../test-plan|test-plan]] — the uncited-claim and SSRF suites
- ADR: [[../adr/0001-fetch-then-synthesise|F8-0001]]
- Reused: [[../../f1-ats-job-discovery/index|F1]] politeness handler, [[../../f3-claude-batch-enrichment/index|F3]] batch machinery

## Scope

**In:** target selection and freshness, seven category fetchers behind one port, SSRF-safe fetching,
content extraction, synthesis, citation verification, warnings, company-stage feedback, presentation
and the on-demand command.
**Out:** contact data, salary benchmarking (backlog), interview preparation (backlog), any
recommendation about whether to apply, paid data providers.

## Module scope

`Domain/Research`, `Domain/Abstractions/IResearchFetcher`, `Application/Research`,
`JobHunter.Scrapers/Research`, `JobHunter.Claude/Prompts/ResearchSynthesisPrompt.cs`,
`Infrastructure/Persistence` (three tables), one command and two endpoints.

## Handoff interfaces

| Produces | Consumer |
|---|---|
| `ResearchCompleted` | F5 digest enrichment |
| `companies.stage` update | F3 enrichment, F4 ranking |
| `company_research` and claims | F5 presentation, F9 facets |

## Tasks

See [[tracker|tracker]]. 9 tasks, ≈ 6 person-days.

## Definition of Done (epic)

- AC-01…AC-10 covered by passing tests.
- **Zero uncited claims** — the uncited-claim suite is green, and the structural assertion holds that
  every stored claim resolves to a source in the same dossier.
- **The SSRF suite is green**, every case asserting the request was not made, including the
  redirect-into-private-space and rebinding cases.
- A sparse document set produces a sparse dossier rather than one padded from model memory.
- Categories with nothing found are recorded explicitly, so absence is visible.
- Cost under $0.05 per dossier, at most five per day.
- **Security review completed** before ship ([[../PRD|PRD]] §6.1).
- Contributes to milestone M5 in [[../../../BACKLOG|BACKLOG]] §1.
