---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, feature/f1-ats-job-discovery, mvp, jobhunter]
---

# ATS endpoint reference

> The five providers F1 consumes, their actual shapes, and the fields the adapters depend on.
> This is the contract the weekly live suite verifies. A change here is an adapter change.

## Greenhouse

```
GET https://boards-api.greenhouse.io/v1/boards/{board_token}/jobs?content=true
```

| Consumed field | Path | Notes |
|---|---|---|
| external id | `jobs[].id` | numeric, stable |
| title | `jobs[].title` | |
| description | `jobs[].content` | HTML-escaped HTML — double-decode before stripping |
| apply url | `jobs[].absolute_url` | |
| location | `jobs[].location.name` | free text, often `"Remote - EMEA"` |
| posted at | `jobs[].updated_at` | ISO-8601; **update**, not creation |
| departments | `jobs[].departments[].name` | |

**Volatile (stripped before hashing):** `updated_at`, `requisition_id`.
**Pagination:** none — the whole board is one response. Large boards reach several MB.
**Board token:** the slug in `boards.greenhouse.io/{token}`.

## Lever

```
GET https://api.lever.co/v0/postings/{board_token}?mode=json
```

| Consumed field | Path | Notes |
|---|---|---|
| external id | `[].id` | UUID |
| title | `[].text` | |
| description | `[].descriptionPlain` | plain text — preferred over `description` |
| apply url | `[].hostedUrl` | |
| location | `[].categories.location` | |
| commitment | `[].categories.commitment` | `Full-time` / `Contract` → employment type |
| posted at | `[].createdAt` | epoch milliseconds |
| workplace type | `[].workplaceType` | `remote` / `hybrid` / `onsite` — the cleanest remote signal of the five |

**Volatile:** none — Lever's payload is stable between fetches.
**Pagination:** none.

## Ashby

```
GET https://api.ashbyhq.com/posting-api/job-board/{board_token}?includeCompensation=true
```

| Consumed field | Path | Notes |
|---|---|---|
| external id | `jobs[].id` | |
| title | `jobs[].title` | |
| description | `jobs[].descriptionPlain` | |
| apply url | `jobs[].applyUrl` | |
| location | `jobs[].location` + `jobs[].secondaryLocations[]` | multi-location is common |
| remote | `jobs[].isRemote` | boolean — authoritative |
| employment type | `jobs[].employmentType` | |
| compensation | `jobs[].compensation.compensationTierSummary` | **the only provider that routinely publishes salary** |
| posted at | `jobs[].publishedAt` | |

**Volatile:** `jobs[].updatedAt`.
**Note:** Ashby's compensation field is the single highest-value structured field across all five
providers; it is worth handling carefully rather than deferring to the model's estimate.

## Workable

```
GET https://apply.workable.com/api/v1/widget/accounts/{board_token}?details=true
```

| Consumed field | Path | Notes |
|---|---|---|
| external id | `jobs[].shortcode` | |
| title | `jobs[].title` | |
| description | `jobs[].description` | HTML |
| apply url | `jobs[].application_url` | |
| location | `jobs[].country`, `jobs[].city`, `jobs[].telecommuting` | structured — the best location data of the five |
| posted at | `jobs[].published_on` | date only, no time |

**Volatile:** none observed.
**Quirk:** `published_on` is a date, so `posted_at` is stored at midnight UTC. Freshness ranking
must tolerate day-level granularity for Workable postings.

## Career pages (JSON-LD) — Tier 2

```
GET {careers_url}   → parse <script type="application/ld+json"> where @type == "JobPosting"
```

Consumed per `schema.org/JobPosting`: `identifier`, `title`, `description`, `url`,
`jobLocation.address.*`, `jobLocationType`, `employmentType`, `baseSalary.value.*`, `datePosted`.

**Confidence:** capped at 0.70 — page structure varies wildly and the identifier is often absent, in
which case the apply URL is hashed to synthesise one.
**Volatile:** `dateModified`, and anything inside `<meta>` tags.

## Detection probes

`AtsProbeDetector` derives candidate tokens from the canonical domain (bare name, hyphenated,
concatenated) and from any `careers_url`, then probes each provider in order of prior likelihood.

| Signal | Weight | Confidence contribution |
|---|---|---|
| Board responds 200 with ≥1 posting | required | 0.60 |
| Board's posting apply URLs point back at the company domain | strong | +0.25 |
| Company careers page links to the board host | strong | +0.10 |
| Token derived exactly from the domain | weak | +0.05 |

A binding needs ≥ 0.80 to be used for discovery. Two providers scoring ≥ 0.80 is
`Ambiguous` — the company stays inactive until a human resolves it (AC-04). This is deliberately
conservative: attributing another company's jobs is a far worse failure than missing a company.

## Related

[[../sad]] §3 · [[../test-plan]] · [[../../../00-overview/adr/0009-ats-first-no-linkedin|ADR-0009]]
