---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f9-search-and-api, mvp, jobhunter]
---

# PRD — f9-search-and-api

> **Inputs:** [[../../CONTEXT]] · [[../../00-overview/sad|SAD]] §3, §5 · [[../../00-overview/idea-brief|idea-brief]] §3, §15
> **External context:** [[../../00-overview/adr/0008-typesense-over-postgres-fts|ADR-0008]], [[../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]], [[../../ARCHITECTURE-OPEN-DECISIONS|O2]]

## 1. Context

After three months the system holds several thousand analysed jobs, their enrichments, matches,
scores and company dossiers. All of it is reachable only through one channel: a ten-card message at
07:00. That is the right primary interface, and it is a bad archive.

The questions that go unanswered are the retrospective ones. *Every Kafka role I have seen in the last
90 days. Which Series-B companies are hiring staff engineers in EMEA. What did I ignore in June that I
would look at now. How has the average salary for my searches moved.* None of these fit a daily digest,
and all of them are cheap once the corpus is indexed.

There is a second audience with a legitimate claim on this feature. The reviewer who opens the
repository because it is on a CV ([[../../00-overview/idea-brief|brief]] §3) will not run the system;
they will read the README and, if it exists, click a live URL. A working OpenAPI document and a
reachable endpoint communicate more in thirty seconds than any amount of documentation. That is a real
requirement, and it is why [[../../ARCHITECTURE-OPEN-DECISIONS|O2]] leans toward internet-facing.

The design constraint that shapes everything: **the index is derived data.** Losing it must be a
rebuild, never a data loss, and no path the digest depends on may read from it
([[adr/0001-index-as-rebuildable-projection|ADR-F9-0001]]).

## 2. Goals

- Make the whole corpus searchable by text, with typo tolerance and facets.
- Expose read models over jobs, companies, applications, runs and preferences through a documented API.
- Provide operational endpoints for the tasks the runbooks need.
- Let the Owner search from Telegram as well as over HTTP.
- Give a reviewer something live and self-describing to look at.

## 3. Non-goals

- Being a source of truth. The index is a projection, always rebuildable from PostgreSQL.
- Public or unauthenticated access. Every endpoint requires a credential.
- A write API for jobs or enrichments. The pipeline owns those; the API exposes reads plus a small set
  of operator actions.
- A user interface. The API is for the Owner's tooling and for a reviewer with `curl`.
- Semantic or vector search. Parked ([[../../00-overview/idea-brief|brief]] §14 item 7).

## 4. User stories

### US-01: Search everything I have seen
**As the** Owner **I want** to search the whole corpus by text **so that** I can find a role I
remember but did not save.

### US-02: Narrow by what matters
**As the** Owner **I want** to filter by technology, company stage, remote policy, salary band and
score **so that** I can answer a specific question rather than skim.

### US-03: Search forgivingly
**As the** Owner **I want** near-miss spellings to still find things **so that** I do not have to
remember exactly how a technology was written.

### US-04: Query from where I am
**As the** Owner **I want** to search from Telegram **so that** I do not need a terminal.

### US-05: Read the data programmatically
**As the** Owner **I want** a documented API over the read models **so that** I can build my own
analysis without touching the database.

### US-06: Operate the system
**As the** operator **I want** endpoints for the actions the runbooks call for **so that** recovery
does not require database access.

### US-07: Show the system to someone
**As the** Owner **I want** a self-describing live endpoint **so that** a reviewer can understand the
system in a minute.

### US-08: Trust that nothing private is exposed
**As the** Owner **I want** certainty that my CV and personal details are not searchable
**so that** exposing an endpoint is not a risk.

## 5. Acceptance criteria

### AC-01 (US-01) — happy path
**Given** an indexed corpus
**When** the Owner searches by text
**Then** matching opportunities are returned ordered by relevance, each with the information needed to
recognise it.

### AC-02 (US-02) — happy path
**Given** a search with filters applied
**When** it is executed
**Then** only opportunities matching every filter are returned, and the available refinements and their
counts are reported.

### AC-03 (US-03) — happy path
**Given** a query containing a misspelling
**When** it is executed
**Then** the intended matches are still returned.

### AC-04 (US-08) — domain invariant
**Given** any searchable content
**When** it is inspected in full
**Then** it contains no CV content and no personal details of the Owner.

### AC-05 (US-05, US-07) — happy path
**Given** the running system
**When** its interface description is requested
**Then** a complete, accurate description of every endpoint, its inputs, outputs and required
permissions is returned.

### AC-06 (US-05) — authorization
**Given** any request to any endpoint other than liveness and readiness
**When** it arrives without a valid credential
**Then** it is refused.

### AC-07 (US-06) — authorization
**Given** a request to an operational endpoint that changes system state
**When** it arrives with only read permission
**Then** it is refused.

### AC-08 (US-01) — cross-context
**Given** an opportunity that has closed
**When** a search is executed
**Then** it is excluded by default and can be included only by asking explicitly.

### AC-09 (US-01) — error path
**Given** the search service is unavailable
**When** a search is attempted
**Then** the failure is reported clearly, and no other part of the system is affected.

### AC-10 (US-06) — cross-context
**Given** the searchable content has diverged from the system of record
**When** a rebuild is requested
**Then** the searchable content is reconstructed entirely from the system of record, and no
information is lost by the rebuild.

### AC-11 (US-04) — happy path
**Given** the Owner searches from the messaging client
**When** results are returned
**Then** they are presented in the same scannable form as the daily digest.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Search latency | < 150 ms p95 for 10 000 documents | Benchmark |
| API read latency | < 300 ms p95 | Benchmark |
| Index freshness | < 5 min behind the system of record | Reconcile metric |
| Full rebuild | < 10 min for 10 000 jobs | Timed operation |
| **CV content in the index** | **0 occurrences** | Automated scan |
| Interface description accuracy | 100% of endpoints present with examples | Generation test |
| Availability impact | index unavailability never affects the pipeline or the digest | Fault-injection test |

## 6.1 Security / privacy

- **Data classification:** the index holds public job data plus internal scores. **No CV content, no
  personal details** — the F4 boundary holds here too.
- **Personal data touched:** none in the index. The API can return application data, which is
  confidential and owner-scoped.
- **AuthZ/AuthN impact:** this is the system's **only inbound HTTP surface**. Every endpoint declares a
  scope; the F0 fallback-deny policy means a new endpoint without one is refused by default (AC-06).
- **Abuse cases:**
  - An endpoint added without a scope → fallback-deny, plus an endpoint-convention test (gate G7).
  - Credential leakage exposing the pipeline → operational endpoints require the higher scope, and the
    subject must match the configured Owner (AC-07).
  - Injection through a search query → parameterised queries and escaped search terms; the search
    client never concatenates user input into a filter expression.
  - Enumeration through sequential ids → UUID v7 keys are not guessable.
  - Denial of service → this is internet-facing behind Cloudflare; rate limiting at the edge plus a
    per-token limit at the API.
- **Security review:** **required** — the only inbound HTTP surface in the system, and the one exposed
  to the internet.

## 7. Metrics / KPIs

- **Search latency p95** — target under 150 ms.
- **Index drift** — documents differing from the system of record. Target near zero, self-healing.
- **Queries per week** — informational; low usage means the digest is doing its job.
- **Authorisation failures** — expected to be near zero; a sustained non-zero on an internet-facing
  endpoint is worth looking at.

## 8. Open questions

- [ ] Internet-facing or cluster-internal? — owner: Viacheslav — *default: internet-facing behind
  Keycloak and Cloudflare; a reviewer clicking a live URL is worth the marginal risk.*
  ([[../../ARCHITECTURE-OPEN-DECISIONS|O2]])
- [ ] Should application notes be indexed? — owner: Viacheslav — *default: no; they may contain
  anything, and the value is low.*
- [ ] Should there be a read-only demo credential for reviewers? — owner: Viacheslav — *default: no
  for M5; the OpenAPI document alone is enough to understand the system.*

## DoD self-check

- [x] Coverage types: happy (01, 02, 03, 05, 11), error (09), authorization (06, 07), domain invariant (04), cross-context (08, 10)
- [x] No implementation tokens in §5 — no HTTP verbs, paths, status codes or JSON
- [x] Every US has ≥1 AC; NFRs measurable
