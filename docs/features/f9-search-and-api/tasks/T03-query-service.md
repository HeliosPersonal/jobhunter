# T03 — Query service, filters and facets

**Layer:** search · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

`TypesenseQueryService`: build filter expressions from typed parameters, escape every
user-supplied term, request facets alongside hits, and exclude closed jobs unless explicitly asked.
User input is **never** concatenated into a filter expression.

## Done when

- Filters are built from typed parameters; a test asserts filter syntax in a query is treated as text (AC-02).
- Facet counts are returned with every search so refinement needs no second round trip.
- Typo tolerance returns intended matches for a misspelled technology (AC-03).
- Closed jobs are excluded by default and included only on an explicit flag (AC-08).
- An unavailable index produces a clear failure, never a partial result presented as complete (AC-09).
- Search p95 stays under 150 ms at 10 000 documents.

## Links

[[../contracts/openapi|API contract]] §Search
