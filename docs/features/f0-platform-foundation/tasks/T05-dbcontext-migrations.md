# T05 — JobHunterDbContext, configuration convention, first migration

**Layer:** infra/db · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

`JobHunterDbContext` with `ApplyConfigurationsFromAssembly`, the `IEntityTypeConfiguration`
convention from [[../data-model|data-model]], Npgsql registration, and an initial empty migration
that establishes `__EFMigrationsHistory` and the `hangfire` schema. Add a design-time factory so
`dotnet ef` works without starting the host.

## Done when

- `dotnet ef migrations add` and `database update` both work against the Aspire-provisioned database.
- The initial migration applies to an empty database in under 5 s (AC-02).
- Enums are configured to persist as `text`; a test asserts no enum maps to an integer column.
- `Guid` keys are `ValueGeneratedNever()` so ids come from `IIdGenerator`.
- A design-time factory exists; `dotnet ef` needs no running infrastructure.

## Out of scope

- Any domain table — later features own theirs.

## Links

[[../data-model]] · [[../../../00-overview/adr/0003-postgresql-efcore-dapper|ADR-0003]]
