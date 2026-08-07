# T02 — Migration and repository

**Layer:** infra/db · **Deps:** T01 · **Est:** S · **Owner:** Viacheslav

## What

Migration `F8_AddCompanyResearch` with the six indexes. The load-bearing detail is
`research_claims.source_id NOT NULL` with a foreign key — the schema-level expression of invariant 5.

## Done when

- Migration applies on a clean database; all six indexes exist with declared names.
- Inserting a claim with a null source is rejected by the database, asserted by attempting it.
- Inserting a claim citing a source from a different dossier is rejected by the foreign key.
- One dossier per company per Run is enforced.
- The freshness lookup is covered by `idx_research_company_latest`, verified with a query plan assertion.

## Implementation

- **Migration** `20260807081508_F8AddCompanyResearch` creates `company_research`, `research_sources` and
  `research_claims` with all six declared indexes (`uq_research_company_run`,
  `idx_research_company_latest`, `idx_sources_research`, `uq_sources_url`, `idx_claims_research`,
  `idx_claims_warnings`). Enums persist as `text`; category lists are `jsonb` name arrays.
- **Invariant 5 in the schema.** `research_claims.source_id` is `NOT NULL`, and a composite foreign key
  `fk_research_claims_source (research_id, source_id) → research_sources (research_id, id)` is declared in
  raw SQL against the source's `(research_id, id)` alternate key. A claim can therefore only cite a source
  in its own dossier — a cross-dossier citation is unrepresentable, not merely rejected. The constraint is
  `DEFERRABLE INITIALLY DEFERRED` so EF, which has no navigation modelling the composite key, may insert
  claims and sources in any order within one `SaveChanges`; the check fires at commit.
- **`CompanyResearch.CategoriesCovered`** is derived by the aggregate from its claims, so EF cannot map it.
  `ResearchRepository` denormalises the current value into a shadow `categories_covered` jsonb column at
  write time for the Dapper read side; a load never writes it back, so it cannot disagree with the claims.
- **Repository.** `ResearchRepository : IResearchRepository` (`Domain/Abstractions`) exposes `Add`,
  `FindLatestAsync(companyId)` (newest by `generated_at`, sources and claims included) and
  `SaveChangesAsync`. There is no update path — a new Run produces a new dossier. Registered scoped in
  `Infrastructure/DependencyInjection`.
- **Tests** (`ResearchPersistenceTests`, 8 Docker integration tests): all six indexes exist after the
  migration; a dossier round-trips with its sources and claims; `FindLatest` returns the newest dossier and
  `null` when never researched; a null-source claim is rejected (`NotNullViolation`); a cross-dossier
  citation is rejected by the composite FK (`ForeignKeyViolation`); a second dossier for the same
  (company, run) is rejected (`uq_research_company_run`); and the freshness lookup is served by
  `idx_research_company_latest` (EXPLAIN with `enable_seqscan = off`).

## Links

[[../data-model]]
