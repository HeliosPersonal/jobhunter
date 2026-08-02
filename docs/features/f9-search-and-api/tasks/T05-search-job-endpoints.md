# T05 — Search, job and CV endpoints

**Layer:** api · **Deps:** T03, T04 · **Est:** M · **Owner:** Viacheslav

## What

`/api/search`, `/api/jobs/{id}`, `/api/jobs/{id}/aliases`, `/api/jobs`, and the owner-scoped CV
endpoints `GET /api/cv` and `POST /api/cv`. The detail endpoint returns the **score components**, not
just the total — the API-side expression of F4's explainability guarantee. Paging is cursor-based on
`(score, id)`; no offset paging. The CV endpoints back F4 AC-06/AC-07 and are exercised by F4 T03.

## Done when

- Search returns hits, found count, facets and a next cursor (AC-01, AC-02).
- Job detail includes the score components and they reconcile to the total.
- `/aliases` shows which raw postings merged into the job, so a suspected bad merge is inspectable.
- Cursors are opaque and reject a cursor from a previous schema version with a clear message.
- A cursor past the end returns an empty page rather than an error.
- Search and job endpoints declare `jobhunter:read` explicitly.
- `GET /api/cv` returns **metadata only** (version, activation date, match count) and never CV
  content; `POST /api/cv` activates a new immutable version and queues the re-match
  ([[../../f4-cv-matching-ranking/adr/0002-cv-versioning-and-restaling|ADR-F4-0002]]).
- Both CV endpoints are owner-scoped: `jobhunter:read` **plus** the `sub` == Owner check; a valid
  `jobhunter:read` token for a different subject is a 403.

## Links

[[../contracts/openapi|API contract]] · [[../../f4-cv-matching-ranking/adr/0001-explainable-linear-scoring|ADR-F4-0001]] ·
[[../../f4-cv-matching-ranking/adr/0002-cv-versioning-and-restaling|ADR-F4-0002]]
