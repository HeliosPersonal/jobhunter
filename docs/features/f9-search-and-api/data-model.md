---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "08"
ticket: ""
tags: [sdlc/stage-08, feature/f9-search-and-api, mvp, jobhunter]
---

# Data model — f9-search-and-api

> **Owns:** no PostgreSQL tables. F9 owns the **Typesense schema**, which is a projection of data owned
> by F1, F2, F3, F4 and F6.
> **References (read-only):** `jobs`, `companies`, `enrichments`, `matches`, `scores`, `applications`,
> `runs`, `preference_weights`.

## Why F9 owns no tables

Every read model it exposes is a Dapper query over tables another feature owns. Every searchable
document is derived from those same tables. Introducing storage here would create a second copy of
data that must be kept correct, for no benefit — the whole point of
[[adr/0001-index-as-rebuildable-projection|ADR-F9-0001]] is that the index is the *only* derived copy,
and it is disposable.

## Typesense collection

`{env}_jobhunter_jobs`, per the helios naming convention.

```json
{
  "name": "production_jobhunter_jobs",
  "fields": [
    { "name": "id",                "type": "string" },
    { "name": "title",             "type": "string", "sort": true },
    { "name": "companyName",       "type": "string", "facet": true },
    { "name": "companyDomain",     "type": "string" },
    { "name": "description",       "type": "string" },
    { "name": "technologies",      "type": "string[]", "facet": true },
    { "name": "countries",         "type": "string[]", "facet": true },
    { "name": "remotePolicy",      "type": "string",   "facet": true },
    { "name": "seniority",         "type": "string",   "facet": true, "optional": true },
    { "name": "employmentType",    "type": "string",   "facet": true },
    { "name": "companyStage",      "type": "string",   "facet": true, "optional": true },
    { "name": "aiUsage",           "type": "string",   "facet": true, "optional": true },
    { "name": "salaryMin",         "type": "int32",    "facet": true, "optional": true },
    { "name": "salaryMax",         "type": "int32",    "optional": true },
    { "name": "salaryCurrency",    "type": "string",   "optional": true },
    { "name": "score",             "type": "float",    "sort": true },
    { "name": "postedAt",          "type": "int64",    "sort": true, "optional": true },
    { "name": "firstSeenAt",       "type": "int64",    "sort": true },
    { "name": "status",            "type": "string",   "facet": true },
    { "name": "applicationStatus", "type": "string",   "facet": true, "optional": true }
  ],
  "default_sorting_field": "score",
  "token_separators": ["-", "/", ".", "#"]
}
```

**`token_separators`** is the detail that makes technology search work in practice. Without it,
`C#` tokenises as `c`, `.NET` as `net`, and `CI/CD` as one opaque token. With `#` and `.` and `/` as
separators, a search for `C#` finds `C#`, and `node.js` finds `node` and `js`.

## What is deliberately absent

| Not indexed | Why |
|---|---|
| **CV content, in any form** | The F4 boundary extends here. QG-2, and the scan suite verifies it |
| Match reasons | They reference the CV implicitly; the risk outweighs the value |
| Missing skills | Same |
| Application notes | May contain anything the Owner typed; low search value ([[PRD]] §8) |
| Interview probability | Internal judgement, not something to search by |
| Preference weights | Internal; exposed through their own endpoint with explanations |
| Raw posting payloads | Enormous, and the normalised job carries everything useful |

`JobDocument` in `JobHunter.Search` is a hand-written record listing exactly the fields above. It is
**not** a mapping from the `Job` aggregate, and that is the point: a new field on `Job` — including one
that might one day carry CV-derived text — cannot reach the index without someone editing this record
(SAD §4 S3).

## Projection

| Document field | Source |
|---|---|
| `id`, `title`, `description`, `status` | `jobs` (F2) |
| `companyName`, `companyDomain`, `companyStage` | `companies` (F1, stage updated by F3/F8) |
| `technologies` | union of `job_technologies` (F2, deterministic) and `enrichments.technologies` (F3, inferred) |
| `countries`, `remotePolicy`, `seniority`, `employmentType`, `salary*` | `jobs` (F2), falling back to `enrichments` estimate where the published value is absent |
| `aiUsage` | `enrichments` (F3) |
| `score` | latest `scores.final_score` (F4); `0` when not yet ranked |
| `applicationStatus` | `applications.status` (F6), absent when none |
| `postedAt`, `firstSeenAt` | `jobs` |

The union in `technologies` is deliberate: the deterministic set is precise, the inferred set is
broader, and for *search* recall matters more than precision. The two remain separable in PostgreSQL
for anything where that distinction matters.

## Reconciliation

Nightly at 04:00: compare live job count in PostgreSQL against document count in Typesense. Divergence
above 1% triggers a re-index of the affected window and emits `jobhunter.index.drift`.

A full rebuild is one operator command — drop, recreate, stream every live job — and completes in under
ten minutes for 10 000 jobs (AC-10, QG-1). Because the index holds nothing that is not derivable, a
rebuild is a routine operation rather than a recovery.

## Related

[[../../architecture/data-model]] · [[sad]] §5 · [[adr/0001-index-as-rebuildable-projection|ADR-F9-0001]] ·
[[../../operations/runbooks|R8]]
