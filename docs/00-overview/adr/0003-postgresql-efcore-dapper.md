---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0003 — PostgreSQL as the single store; EF Core for writes, Dapper for reads

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The plan names both EF Core and Dapper. The system has two very different data workloads: a
write side with real aggregates, invariants and an evolving schema (Companies, Jobs, Runs,
Applications), and a read side of wide analytical projections (the digest query joins Job,
Enrichment, Match, Score, PreferenceModel and Application in one shot). We must decide whether to
pick one tool or use both, and where the boundary sits. Helios provides one shared PostgreSQL
instance; the convention is one scoped database per app per environment.

## Decision drivers

- Schema will change frequently across ten features — migrations must be first-class, not hand-written DDL.
- The digest and analytics queries are wide, read-only, and performance-sensitive; EF's change
  tracker and LINQ translation buy nothing there and cost clarity.
- Money must be exact (`numeric`), timestamps must be `timestamptz`, ids must be time-ordered
  ([[0015-uuidv7-keys-and-timestamptz|ADR-0015]]).
- One datastore to back up, one connection string, one transaction boundary.
- `wisewizard` uses Dapper-only and pays for it in hand-written schema evolution; `overflow` uses
  EF-only and pays for it in awkward read models. Take the lesson from both.

## Considered options

1. **EF Core only.**
2. **Dapper only, hand-written idempotent schema script** (the `wisewizard` approach).
3. **EF Core for writes and migrations, Dapper for read models.**
4. **EF Core writes + a separate read store (Typesense/materialised views) for all reads.**

## Decision outcome

**Chosen: Option 3.**

- **EF Core** owns the schema (`JobHunterDbContext`, `IEntityTypeConfiguration<T>` per aggregate,
  EF migrations applied by an init container), all aggregate writes, and the Wolverine outbox
  integration. Every invariant that needs a transaction goes through EF.
- **Dapper** owns read models: the digest projection, the ranking query, the analytics endpoints and
  the preference-learning feature extraction. These live in
  `Infrastructure/Persistence/Queries/*Query.cs`, return flat DTOs, and are integration-tested
  against a real Postgres via Testcontainers.

The rule that keeps this from becoming mush: **Dapper never writes.** A single grep for
`ExecuteAsync` in the query folder is the enforcement, backed by an architecture test.

Option 4 is partly adopted anyway — Typesense is a projection for *search*
([[0008-typesense-over-postgres-fts|ADR-0008]]) — but it is never a source of truth and the digest
never reads from it.

## Consequences

**Positive**
- Migrations are versioned, reviewable and reversible; schema evolution across ten features is cheap.
- The expensive read paths are plain SQL that can be `EXPLAIN`ed and tuned.
- One database: one backup, one restore drill, one connection string, one Hangfire schema alongside.

**Negative**
- Two persistence idioms in one codebase; a reader must know which side of the line they are on.
  Mitigated by folder separation (`Repositories/` vs `Queries/`) and the no-writes rule.
- Integration tests need Docker (Testcontainers). CI provides it; local contributors must have it.

**Neutral**
- Hangfire shares the same database under a dedicated `hangfire` schema, following the `wisewizard`
  precedent ([[0004-hangfire-scheduling|ADR-0004]]).

## Links

- SAD: [[../sad]] §4 S3, §8
- Data model: [[../../architecture/data-model]]
- Related: [[0004-hangfire-scheduling]], [[0007-transactional-outbox]], [[0015-uuidv7-keys-and-timestamptz]]
