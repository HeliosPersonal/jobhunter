---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, feature/f9-search-and-api, mvp, jobhunter]
---

# API contract

> Every endpoint, its scope and its shape. The generated OpenAPI document is asserted against the
> registered endpoints in a test, so this table and reality cannot drift ([[../sad|SAD]] §4 S6).

## Conventions

- Base path `/api`. Served at `https://jobhunter.devoverflow.org/api`.
- Bearer JWT from Keycloak realm `jobhunter`, audience `jobhunter-api`. The `sub` claim must match the
  configured Owner subject — a valid token for another subject is refused.
- **Fallback policy is `RequireAuthenticatedUser`.** An endpoint added without an explicit scope is
  refused by default rather than open by default.
- Errors are RFC 7807 problem details with no internal detail in the body.
- Pagination is cursor-based on `(score, id)`. No offset paging — it is wrong under concurrent writes
  and encourages deep scans.
- Interactive documentation at `/scalar`; the raw document at `/openapi/v1.json`.

## Endpoints

### Search

| Method | Path | Scope | Purpose |
|---|---|---|---|
| `GET` | `/api/search` | `jobhunter:read` | Full-text search with filters and facets |

```
GET /api/search
  ?q=kafka distributed
  &technology=Kafka&technology=Azure
  &companyStage=SeriesB&companyStage=SeriesC
  &remotePolicy=Remote
  &country=DE&country=NL
  &minScore=70
  &salaryMin=150000
  &includeClosed=false          # default false (AC-08)
  &sort=score:desc
  &cursor=eyJzIjo4Ny4yLCJpIjoiMDE5MmU4In0
  &limit=20
```

```json
{
  "hits": [
    {
      "id": "0192e8b7-...",
      "title": "Staff Backend Engineer",
      "companyName": "Snowflake",
      "companyDomain": "snowflake.com",
      "technologies": ["Kafka", "Azure", "C#"],
      "remotePolicy": "Remote",
      "seniority": "Staff",
      "companyStage": "Public",
      "salaryMin": 180000, "salaryMax": 220000, "salaryCurrency": "USD",
      "score": 95.0,
      "status": "Live",
      "applicationStatus": "Applied",
      "postedAt": "2026-07-28T00:00:00Z",
      "highlight": "experience with <mark>Kafka</mark> and distributed systems"
    }
  ],
  "found": 47,
  "facets": {
    "technologies":  [ { "value": "Kafka", "count": 47 }, { "value": "Azure", "count": 31 } ],
    "companyStage":  [ { "value": "SeriesB", "count": 22 } ],
    "remotePolicy":  [ { "value": "Remote", "count": 38 } ]
  },
  "nextCursor": "eyJzIjo3MS4wLCJpIjoiMDE5MmY0In0"
}
```

Facets are returned with every search so the client can offer refinements without a second round trip
(AC-02).

### Jobs

| Method | Path | Scope | Purpose |
|---|---|---|---|
| `GET` | `/api/jobs/{id}` | `jobhunter:read` | Full detail: job, enrichment, match, score components |
| `GET` | `/api/jobs/{id}/aliases` | `jobhunter:read` | Which raw postings merged into this job |
| `GET` | `/api/jobs` | `jobhunter:read` | Recent jobs, cursor-paged |

`/api/jobs/{id}` returns the **score components** as well as the total — the API-side expression of
[[../../f4-cv-matching-ranking/adr/0001-explainable-linear-scoring|ADR-F4-0001]]'s explainability
guarantee. `/aliases` exists so a suspected bad merge can be inspected without database access
([[../../f2-normalization-dedup/PRD|F2]] AC-08).

### Companies

| Method | Path | Scope | Purpose |
|---|---|---|---|
| `GET` | `/api/companies/{domain}` | `jobhunter:read` | Company, its ATS binding, live jobs, latest dossier |
| `GET` | `/api/companies/{domain}/research` | `jobhunter:read` | The dossier with every claim, its source and its date |
| `POST` | `/api/companies` | `jobhunter:admin` | Add a company to the registry |
| `POST` | `/api/companies/{domain}/research` | `jobhunter:admin` | Request research |

### Applications

Defined in [[../../f6-application-tracking/contracts/application-api|the F6 contract]]. Listed here
because they share this document and this auth model.

### Runs

| Method | Path | Scope | Purpose |
|---|---|---|---|
| `GET` | `/api/runs` | `jobhunter:read` | Recent runs with state, cost and counts |
| `GET` | `/api/runs/{id}` | `jobhunter:read` | One run: batches, ledger, per-stage timings |
| `POST` | `/api/runs/{id}/resume` | `jobhunter:admin` | Resume a non-terminal run ([[../../../operations/runbooks\|R1]]) |
| `POST` | `/api/runs/{id}/redeliver` | `jobhunter:admin` | Re-deliver a digest ([[../../../operations/runbooks\|R1]]) |

`redeliver` is safe by construction: the delivery log means already-sent cards are not sent again
([[../../f5-daily-digest-telegram/adr/0002-delivery-idempotence|ADR-F5-0002]]).

### Preferences

| Method | Path | Scope | Purpose |
|---|---|---|---|
| `GET` | `/api/preferences` | `jobhunter:read` | Active model with every weight and its evidence |
| `GET` | `/api/preferences/suppressed` | `jobhunter:read` | What was hidden, and why |
| `POST` | `/api/preferences/weights/{id}/disable` | `jobhunter:admin` | Disable one weight |
| `POST` | `/api/preferences/reset` | `jobhunter:admin` | Deactivate the model; signals are retained |

`GET /api/preferences` renders each weight with its supporting evidence — the API face of
[[../../f7-preference-learning/adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]]:

```json
{
  "version": 7, "signalCount": 412, "fittedAt": "2026-07-28T03:00:00Z",
  "weights": [
    {
      "id": "0192f8...", "dimension": "SalaryBand", "value": "below-170k",
      "weight": -0.62, "supportingSignalCount": 38, "positiveRate": 0.105,
      "explanation": "34 of your last 38 actions on roles below 170k EUR were ignores.",
      "disabled": false
    }
  ]
}
```

### Operations

| Method | Path | Scope | Purpose |
|---|---|---|---|
| `POST` | `/api/admin/search/reindex` | `jobhunter:admin` | Full index rebuild ([[../../../operations/runbooks\|R8]]) |
| `POST` | `/api/admin/sources/{id}/unquarantine` | `jobhunter:admin` | Release a quarantined source ([[../../../operations/runbooks\|R4]]) |
| `POST` | `/api/admin/jobs/reprocess` | `jobhunter:admin` | Re-normalise a date window ([[../../f2-normalization-dedup/PRD\|F2]] AC-09) |
| `GET` | `/api/admin/stats` | `jobhunter:admin` | Corpus counts, cost trend, index drift |

Every action a runbook calls for has an endpoint, so recovery does not require database access
(AC-07, US-06).

### Health

| Method | Path | Scope |
|---|---|---|
| `GET` | `/alive` | anonymous |
| `GET` | `/ready` | anonymous |
| `GET` | `/health` | `jobhunter:admin` |

`/alive` and `/ready` are the only anonymous endpoints in the system — the kubelet needs them, and
they expose no business data.

## Errors

```json
{
  "type": "https://jobhunter.devoverflow.org/errors/search-unavailable",
  "title": "Search is temporarily unavailable",
  "status": 503,
  "detail": "The search index could not be reached. Other functionality is unaffected."
}
```

| Status | When |
|---|---|
| `400` | Malformed query or filter |
| `401` | Absent or invalid token |
| `403` | Valid token, insufficient scope, or a subject that is not the Owner |
| `404` | Resource not found |
| `409` | Conflicting state, e.g. a refused application transition |
| `429` | Rate limited |
| `503` | A dependency is unavailable — stated plainly, with the rest of the system unaffected (AC-09) |

## Related

[[../sad]] §5 · [[../test-plan]] · [[../../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]] ·
[[../../../engineering/security]] §2
