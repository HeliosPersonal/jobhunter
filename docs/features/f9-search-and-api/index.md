---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f9-search-and-api, mvp, jobhunter]
---

# F9 · Search & Public API

> **Feature index (MOC).** Every artifact for this feature, in reading order.

The corpus becomes queryable. Everything the pipeline has learned — 5 000 jobs, their enrichments,
matches, scores and company dossiers — sits in PostgreSQL where only the digest can see it. F9 makes
it searchable with typo tolerance and facets, and exposes it through a documented, Keycloak-protected
HTTP API.

It also serves the reviewer audience directly: a live URL and an OpenAPI document are the fastest way
for someone to understand what this system does ([[../../00-overview/idea-brief|brief]] §3).

## Reading order

1. [[PRD|PRD]] — what must be searchable, and what must never be
2. [[sad|SAD]] — the index as a projection, endpoint design, auth
3. [[data-model|Data model]] — the Typesense schema (F9 owns no tables)
4. [[contracts/openapi|OpenAPI]] — every endpoint, scope and shape
5. [[test-plan|Test plan]] — the index-drift and no-CV-in-index suites
6. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 10 tasks

## Architecture decisions

- [[../../00-overview/adr/0008-typesense-over-postgres-fts|ADR-0008]] — Typesense over Postgres FTS
- [[adr/0001-index-as-rebuildable-projection|ADR-F9-0001]] — the index is derived data, never a source of truth

## Milestone

M5 — Compounding. Can be pulled forward if a live demo URL is needed earlier — it only needs
normalised jobs.

## Related

[[../f8-company-research-agent/index|← F8]] · [[../f2-normalization-dedup/index|F2]] (the source of truth) ·
[[../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]]
