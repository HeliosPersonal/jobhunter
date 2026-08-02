---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f9-search-and-api, mvp, jobhunter]
---

# Test plan — f9-search-and-api

> Two suites define this feature: the **no-CV-in-index scan** (QG-2) and the **rebuild equivalence
> test** (QG-1). A third — the endpoint-convention test — is what keeps an unprotected endpoint from
> ever shipping.

## Levels

| Level | Scope | Docker | Tooling |
|---|---|---|---|
| Unit | Document projection, filter building and escaping, cursor encoding | No | xUnit |
| Integration | Indexing, querying, facets, typo tolerance, reconcile | Yes | Testcontainers (Typesense + Postgres) |
| **Index scan** | Dump the whole index; assert no CV sentinel and no unexpected field | Yes | Testcontainers |
| **Rebuild** | Drop and reconstruct; assert document-by-document equivalence | Yes | Testcontainers |
| API | Every endpoint, every scope, cursor paging, problem details | Yes | `WebApplicationFactory` |
| **Convention** | Every registered endpoint declares a scope; OpenAPI matches reality | No | Reflection over the endpoint registry |
| Fault injection | Typesense unavailable during a full pipeline run | Yes | Testcontainers |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `Search_ReturnsRelevanceOrderedMatches_WithRecognisableFields` | Integration |
| AC-02 | `Search_WithFilters_ReturnsOnlyMatching_AndReportsFacetCounts` | Integration |
| AC-03 | `Search_WithMisspelling_StillReturnsIntendedMatches` | Integration |
| AC-04 | `IndexContainsNoCvContent_AndNoOwnerPersonalDetail` | **Index scan** |
| AC-05 | `OpenApiDocument_DescribesEveryRegisteredEndpoint_WithExamples` | **Convention** |
| AC-06 | `EveryEndpointExceptHealth_RefusesWithoutCredential` | **Convention** + API |
| AC-07 | `AdminEndpoint_WithReadScopeOnly_IsRefused` | API |
| AC-08 | `ClosedJobs_AreExcludedByDefault_AndIncludedOnlyOnRequest` | Integration |
| AC-09 | `SearchUnavailable_ReportsClearly_AndPipelineIsUnaffected` | **Fault injection** |
| AC-10 | `Rebuild_ReconstructsFromPostgres_WithNoInformationLost` | **Rebuild** |
| AC-11 | `TelegramSearch_RendersResultsInDigestCardForm` | API + rendering |

## The no-CV-in-index scan

The same sentinel technique as F4's leakage suite, applied to the index:

```
1. Activate the sentinel-laden CV from F4's test data.
2. Run a full pipeline: discovery, enrichment, matching, ranking, indexing.
3. Dump every document in the collection.
4. Assert: zero sentinel occurrences in any field of any document.
5. Assert: the set of fields present exactly equals JobDocument's declared fields —
   nothing extra has appeared.
```

Step 5 is the one that catches future mistakes. The sentinel check proves today's index is clean; the
field-set assertion proves nobody has widened the projection since, which is how a leak would actually
happen — someone maps the aggregate instead of the allowlist and everything comes along.

Also asserted: `matches.reasons` and `missing_skills` are absent, because both reference the CV
implicitly ([[data-model]] §What is deliberately absent).

## The rebuild equivalence test

QG-1, and the reason index loss is not an incident:

```
1. Index 1 000 jobs through the normal event path.
2. Snapshot every document.
3. Delete the collection entirely.
4. Run the rebuild command.
5. Assert document-by-document equivalence with the snapshot.
6. Assert the rebuild completed within the time budget.
```

Equivalence, not merely count, is the assertion. A rebuild that produces the right number of
subtly-different documents would pass a count check and fail the product.

## The endpoint-convention test

Reflection over the registered endpoint data sources, asserting:

- Every endpoint except `/alive` and `/ready` has an authorisation policy (gate G7, AC-06).
- Every endpoint appears in the generated OpenAPI document (AC-05).
- Every endpoint has at least one documented response example.
- No endpoint accepts a raw string that reaches a filter expression without escaping.

This is what makes "someone adds an endpoint and forgets the scope" a build failure rather than a
security finding. The fallback-deny policy makes it fail closed at runtime; this test makes it fail
at build time, which is better.

## Edge cases / error paths

- Empty query with filters only → filters applied, results returned; an empty `q` is not an error.
- Query matching nothing → empty result set with facet counts of zero, not a 404.
- Query with 500 characters → truncated at a word boundary; no error.
- Query containing filter syntax → escaped; a test asserts it is treated as text, not as a filter.
- A cursor from a previous schema version → rejected with a clear message rather than a wrong page.
- A cursor pointing past the end → empty page, no error.
- Facet on a field with 10 000 distinct values → capped at the top 20 by count.
- A job indexed before it was ranked → `score` is 0 and it still appears; ranking updates it later.
- A job closed between indexing and querying → excluded by the default filter (AC-08).
- Typesense returns a partial result under load → reported as-is with a partial flag, never silently truncated.
- Two indexers racing on one job → last write wins; the document id is the job id, so no duplicate.
- Reconcile during an active rebuild → the rebuild takes a lock; reconcile skips and logs.
- A token valid for another Keycloak subject → refused with 403, not 200.

## Test data

- 1 000 synthetic jobs with realistic technology, location and salary distributions.
- F4's sentinel CV for the index scan.
- Recorded Typesense responses for unit-level query building.
- A fault-injection Typesense stub returning connection failures on demand.

## NFR validation

- Search p95 under 150 ms at 10 000 documents → benchmark with a ceiling.
- API read p95 under 300 ms → benchmark per endpoint.
- Index freshness under 5 minutes → measured from event publication to document availability.
- Full rebuild under 10 minutes for 10 000 jobs → timed in the rebuild test.
- **Zero CV content in the index** → the index scan; any hit fails the build.
- OpenAPI accuracy 100% → the convention test.
- **Availability impact zero** → the fault-injection test asserts a delivered digest with the index down.

## CI

- **PR:** all levels including the index scan, rebuild and convention tests.
- **Pre-ship:** security review ([[PRD]] §6.1) — this is the only internet-facing surface in the system.

## Related

[[../../engineering/testing-strategy]] · [[contracts/openapi]] · [[../../engineering/security]] §2 · [[sad]] §10
