# T07 — Repository and Dapper query conventions

**Layer:** infra/db · **Deps:** T06 · **Est:** S · **Owner:** Viacheslav

## What

Establish both persistence idioms with one reference implementation each, so later
features copy rather than invent: `Persistence/Repositories/` (EF Core, writes, aggregate-scoped)
and `Persistence/Queries/` (Dapper, reads, flat DTOs). Add `NpgsqlConnectionFactory` for the Dapper
side, sharing the same connection string.

## Done when

- One reference repository and one reference query exist, each with an integration test.
- The Dapper side has no write path; the architecture test in T12 asserts it.
- Both idioms use the same connection string and the same `IClock`.
- Dapper DTOs are `record` types in the query file, not shared models.
- Folder placement is documented in [[../../../engineering/coding-standards|standards]] §3.

## Links

[[../../../00-overview/adr/0003-postgresql-efcore-dapper|ADR-0003]] · [[../../../engineering/coding-standards]] §3
