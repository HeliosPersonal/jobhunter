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

## Links

[[../data-model]]
