---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "S"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0008 — Typesense for job search, over PostgreSQL full-text search

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The plan lists `JobHunter.Search` as a project. Beyond the daily digest, the Owner needs to search
the accumulated corpus — "every Kafka role I have seen in the last 90 days", "Series B companies
hiring staff engineers in EMEA" — with typo tolerance and faceting. Helios already runs a shared
Typesense instance with a per-project collection-prefix convention.

## Decision drivers

- Zero new infrastructure: Typesense is already provisioned, monitored and backed up on helios.
- Facets (company stage, technology, remote policy, salary band) are a first-class need, and
  hand-rolled faceting over Postgres means a `GROUP BY` per facet per query.
- Typo tolerance matters for technology names, where the corpus is inconsistent (`dotnet`, `.NET`, `C#`).
- The index is derived data — losing it must be a rebuild, never a data loss.

## Considered options

1. **PostgreSQL `tsvector` + GIN index.**
2. **Typesense (shared helios instance), collection `{env}_jobhunter_jobs`.**
3. **Elasticsearch / OpenSearch.**
4. **No search; the digest and SQL filters are enough.**

## Decision outcome

**Chosen: Option 2.** Typesense, collection `{env}_jobhunter_jobs`, per the helios naming convention.

The index is a **projection, never a source of truth**. It is written by a subscriber to
`JobIndexRequested` after ranking, and it is fully rebuildable from PostgreSQL by a single admin
endpoint (`POST /api/admin/search/reindex`). A nightly reconcile job compares document counts and
re-indexes drift. No read path that the digest depends on ever touches Typesense.

Postgres FTS is genuinely capable here and would have been chosen if Typesense were not already
running; the deciding factor is that faceting and typo tolerance would each be a hand-rolled
subsystem, and the operational cost of Typesense is already paid. Elasticsearch is rejected as
strictly more machine and more operations for the same outcome.

## Consequences

**Positive**
- Faceted, typo-tolerant search with almost no code; `JobHunter.Search` stays a thin adapter.
- Index drift is a self-healing condition rather than an incident.
- No new cluster component.

**Negative**
- A second store to keep consistent with PostgreSQL (SAD §11 D6). Bounded by the rebuildability
  guarantee and the reconcile job.
- Typesense schema changes require a re-index; the admin endpoint makes that a one-command operation.

**Neutral**
- The collection prefix keeps JobHunter isolated from sibling projects on the shared instance.

## Links

- SAD: [[../sad]] §5, §7, §11 D6
- Feature: [[../../features/f9-search-and-api/index|F9]]
- Infrastructure: [[../../operations/infrastructure]]
