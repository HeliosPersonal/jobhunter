---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f9-search-and-api, mvp, jobhunter]
---

# Epic — F9 Search & Public API

Make the corpus queryable — full text with typo tolerance and facets over Typesense — and expose the
read models through a documented, Keycloak-protected HTTP API with an endpoint for every action the
runbooks need.

Three properties define it:

1. **The index is derived data.** Losing it is a ten-minute rebuild, not a data loss, and nothing the
   digest depends on ever reads from it.
2. **Nothing private is exposed.** The CV boundary drawn in F4 extends here, enforced by an explicit
   field allowlist and verified by a sentinel scan over the whole index.
3. **No endpoint ships without a scope.** Fallback-deny makes it fail closed at runtime; the
   convention test makes it fail at build time.

This is also the feature that serves the reviewer audience directly — a live URL and an accurate
OpenAPI document explain the system faster than any prose
([[../../../00-overview/idea-brief|brief]] §3).

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-08, AC-01…AC-11
- SAD: [[../sad|sad]] — index as projection, endpoint design, auth
- Data model: [[../data-model|data-model]] — the Typesense schema and what is deliberately absent
- Contract: [[../contracts/openapi|API contract]] — every endpoint, scope and shape
- Test plan: [[../test-plan|test-plan]] — index scan, rebuild, convention suites
- ADRs: [[../../../00-overview/adr/0008-typesense-over-postgres-fts|0008]],
  [[../../../00-overview/adr/0014-keycloak-api-telegram-allowlist|0014]],
  [[../adr/0001-index-as-rebuildable-projection|F9-0001]]

## Scope

**In:** the Typesense schema and indexer, the query service with filters and facets, the API host with
auth and OpenAPI, read endpoints over jobs, companies, runs, applications and preferences, operational
endpoints, the Telegram search command, reconcile and rebuild.
**Out:** any write path for pipeline data, a user interface, semantic or vector search, public
unauthenticated access.

## Module scope

`Domain/Abstractions/{ISearchIndex,ISearchQuery}`, the whole `JobHunter.Search` project, the whole
`JobHunter.Api` host, `Infrastructure/Persistence/Queries`, one command in `JobHunter.Telegram`.
**F9 owns no PostgreSQL tables.**

## Handoff interfaces

| Consumes | From |
|---|---|
| `JobIndexRequested` | F2, F4 |
| `JobClosed` | F2 |
| `ApplicationStatusChanged` | F6 |
| Read models over eight tables | F1–F8, all read-only |

F6's application endpoints and F7's preference endpoints are served by **this feature's API host** —
they depend on **F9 T04** (auth, fallback-deny, OpenAPI) for their wiring, even though their handlers
and contracts are owned by F6 and F7 respectively.

## Tasks

See [[tracker|tracker]]. 10 tasks, ≈ 5.0 person-days. T05 also carries the owner-scoped CV endpoints
(`GET`/`POST /api/cv`, backing F4 AC-06/07); T06 also carries the Run start/abort endpoints
(`POST /api/runs`, `POST /api/runs/{id}/abort`, backing F3 AC-12) — see [[../contracts/openapi|the API
contract]].

## Definition of Done (epic)

- AC-01…AC-11 covered by passing tests.
- **Zero CV content in the index**, and the index's field set exactly equals `JobDocument`'s — so a
  future widening of the projection fails the build.
- **A full rebuild reconstructs the index with document-by-document equivalence** in under ten minutes.
- **Every endpoint except health declares a scope**, asserted by the convention test, which is itself
  proven able to fail.
- The OpenAPI document covers every registered endpoint with an example, and is asserted against reality.
- A full pipeline run with Typesense unavailable still delivers the 07:00 digest.
- Every runbook action has an endpoint, so recovery does not require database access.
- **Security review completed** before ship ([[../PRD|PRD]] §6.1) — the only internet-facing surface.
- Completes milestone M5 in [[../../../BACKLOG|BACKLOG]] §1.
