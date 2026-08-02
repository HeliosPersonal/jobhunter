# T02 — Typesense schema and indexer

**Layer:** search · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

The collection schema with its facets and `token_separators`, and `TypesenseIndexer`
consuming `JobIndexRequested`. Indexing is best-effort: it retries, then dead-letters, and never
propagates a failure into the pipeline.

## Done when

- The collection is created idempotently with the schema from [[../data-model|data-model]].
- `token_separators` make `C#`, `.NET` and `CI/CD` searchable as intended — asserted directly.
- Upsert is idempotent; the document id is the job id, so two racing indexers produce one document.
- An unavailable index causes retry then dead-letter, and never fails a pipeline handler (QG-3).
- Deletion on `JobClosed` removes the document.
- Indexing lag stays under 5 minutes, measured from event publication.

## Links

[[../../../00-overview/adr/0008-typesense-over-postgres-fts|ADR-0008]] · [[../sad]] §6.1
