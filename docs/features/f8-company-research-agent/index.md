---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f8-company-research-agent, mvp, jobhunter]
---

# F8 · Company Research Agent

> **Feature index (MOC).** Every artifact for this feature, in reading order.

When a company appears near the top of the digest, the next question is always the same: *who are
they, are they healthy, what is it like to work there, and what will the process be?* F8 answers it
automatically — funding, engineering blog, open source, reviews, recent news, layoffs, stack and
interview process — and it answers it **with a URL beside every claim**.

That last part is the whole feature. A dossier of plausible unsourced statements about a company is
worse than no dossier, because the Owner might act on it.

## Reading order

1. [[PRD|PRD]] — what a dossier must contain, and what may never appear in one
2. [[sad|SAD]] — fetch-then-synthesise, the citation guarantee, freshness
3. [[data-model|Data model]] — `company_research`, `research_claims`, `research_sources`
4. [[contracts/research-schema|Research output contract]] — schema, prompt, citation rules
5. [[test-plan|Test plan]] — the uncited-claim suite
6. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 9 tasks

## Architecture decisions

- [[adr/0001-fetch-then-synthesise|ADR-F8-0001]] — curated fetchers plus synthesis, never open web search

## Milestone

M5 — Compounding.

## Related

[[../f7-preference-learning/index|← F7]] · [[../f9-search-and-api/index|F9 →]] · [[../../CONTEXT]] invariant 5
