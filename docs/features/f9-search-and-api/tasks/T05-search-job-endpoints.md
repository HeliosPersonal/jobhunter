# T05 — Search and job endpoints

**Layer:** api · **Deps:** T03, T04 · **Est:** M · **Owner:** Viacheslav

## What

`/api/search`, `/api/jobs/{id}`, `/api/jobs/{id}/aliases` and `/api/jobs`. The detail
endpoint returns the **score components**, not just the total — the API-side expression of F4's
explainability guarantee. Paging is cursor-based on `(score, id)`; no offset paging.

## Done when

- Search returns hits, found count, facets and a next cursor (AC-01, AC-02).
- Job detail includes the score components and they reconcile to the total.
- `/aliases` shows which raw postings merged into the job, so a suspected bad merge is inspectable.
- Cursors are opaque and reject a cursor from a previous schema version with a clear message.
- A cursor past the end returns an empty page rather than an error.
- Every endpoint declares `jobhunter:read` explicitly.

## Links

[[../contracts/openapi|API contract]] · [[../../f4-cv-matching-ranking/adr/0001-explainable-linear-scoring|ADR-F4-0001]]
