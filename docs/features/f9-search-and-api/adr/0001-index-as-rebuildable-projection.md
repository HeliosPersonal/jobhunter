---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f9-search-and-api, jobhunter]
---

# F9-0001 — The search index is a rebuildable projection, never a source of truth

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

[[../../../00-overview/adr/0008-typesense-over-postgres-fts|ADR-0008]] chose Typesense for search. That
introduces the system's only second copy of data, and second copies are where correctness goes to die:
they drift, they need their own backups, they develop their own failure modes, and eventually
something starts depending on them.

This ADR fixes the rules that keep the copy disposable.

## Decision drivers

- Typesense on the shared helios instance has no backup of its own; it is treated as reconstructible.
- The 07:00 digest must never fail because a search component is down (QG-3 — the digest is the product).
- Drift between the two stores is inevitable; it should be a self-healing condition, not an incident.
- A new field on `Job` must not be able to reach the index by accident, because the one thing that must
  never be indexed is CV-derived text.

## Considered options

1. **Dual-write** — the pipeline writes to PostgreSQL and Typesense in the same operation.
2. **Typesense as the read model of record** for job queries, PostgreSQL for writes only.
3. **Typesense as a subscriber-maintained projection**, fully rebuildable, never on a critical path.
4. **Periodic bulk sync** — dump and reload nightly.

## Decision outcome

**Chosen: Option 3**, with four rules:

1. **The index is written by a subscriber to `JobIndexRequested`**, never by the pipeline directly.
   Indexing failure is retried, then dead-lettered, and never propagates. A day with the index down is
   a day with stale search and a perfectly normal digest.
2. **Nothing the digest depends on reads from the index.** The digest query goes to PostgreSQL. This is
   asserted by a fault-injection test that runs a full pipeline with Typesense unreachable and requires
   a delivered digest.
3. **`JobDocument` is a hand-written record listing every indexed field explicitly.** It is not a
   mapping from the `Job` aggregate. Adding a field to `Job` cannot reach the index without someone
   editing this record — which is the structural half of "no CV content in the index", and it is
   backed by a test asserting the index's field set exactly equals the record's.
4. **A full rebuild is one command** that drops the collection, recreates it and streams every live job
   from PostgreSQL, in under ten minutes for 10 000 jobs. It is a routine operation, not a recovery
   procedure — and the test asserts document-by-document equivalence, not merely a matching count.

A nightly reconcile compares counts and re-indexes any window that has drifted, so the common case
heals itself without anyone noticing.

Option 1 makes every pipeline write depend on Typesense being up, which is exactly backwards. Option 2
would make an unbacked-up store authoritative. Option 4 has a nightly staleness window for no benefit
over event-driven indexing plus reconciliation.

## Consequences

**Positive**
- Losing the index entirely costs ten minutes, not data. It needs no backup and no restore drill.
- Search availability is fully decoupled from pipeline availability.
- Drift self-heals nightly and is visible as a metric when it does not.
- The explicit field allowlist makes accidental exposure structurally difficult rather than merely
  forbidden.

**Negative**
- Search results can lag PostgreSQL by up to the retry window. Irrelevant for a retrospective query tool.
- Two stores to reason about when debugging "why is this not in search". The reconcile metric and the
  one-command rebuild make that a short conversation.

**Neutral**
- The same pattern would extend to any future read model without revisiting this decision.

## Links

- [[../../../00-overview/adr/0008-typesense-over-postgres-fts|ADR-0008]] · [[../sad]] §10 QG-1, QG-3
- [[../data-model]] §What is deliberately absent · [[../test-plan]] §The rebuild equivalence test
- [[../../../operations/runbooks|R8]]
