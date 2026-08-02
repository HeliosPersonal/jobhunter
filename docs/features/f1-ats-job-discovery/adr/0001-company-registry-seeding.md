---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f1-ats-job-discovery, jobhunter]
---

# F1-0001 — Curated seed list plus weekly directory expansion

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Coverage is the hard ceiling on the whole product: a company absent from the registry is invisible
regardless of how good the ranking is. But the registry is also the one part of the system that
cannot be fully automated — "companies worth working for" is a judgement, not a query. Resolves
[[../../../ARCHITECTURE-OPEN-DECISIONS|O1]].

## Decision drivers

- Precision beats recall for the top of the ranking ([[../../../DECISION-LOG|D5]]), which argues for curation.
- A purely manual list decays: it is accurate on day one and stale by month three.
- Detection is cheap (a handful of HTTP probes); curation is expensive (human judgement).
- The registry must be reviewable — a bad entry should be visible in a diff.

## Considered options

1. **Curated YAML only**, hand-maintained in the repository.
2. **Directory crawl only** — enumerate every company on the public ATS directories.
3. **Curated seed plus a weekly expansion crawl**, with expansion entries flagged by provenance.
4. **Import from a paid company-data provider.**

## Decision outcome

**Chosen: Option 3.**

- `tools/seed/companies.yaml` holds ~300 curated companies: domain, display name, optional careers
  URL and country. It is committed, reviewed in diffs, and is the authoritative list.
- A weekly job enumerates the public ATS board directories, proposes companies not already in the
  registry, and inserts them with `source = 'DirectoryCrawl'` and `is_active = false`.
- Proposed companies are surfaced in a weekly digest section. Activating one is an explicit action;
  the crawl never activates anything by itself.
- `source` is stored on every company, so "how did this get here" is always answerable, and a bad
  crawl can be reverted by provenance.

Option 4 is rejected on cost and on the observation that a paid list optimises for completeness,
which is the axis this product deliberately does not compete on.

## Consequences

**Positive**
- The high-signal core is human-chosen; the long tail grows without effort.
- Provenance makes registry quality debuggable and a bad batch revertible.
- The curated file is a reviewable artifact rather than opaque database state.

**Negative**
- Weekly manual triage of proposed companies. Bounded to a few minutes by surfacing it in the digest.
- Crawled entries sit inactive until reviewed, so coverage lags discovery by up to a week. Acceptable.

**Neutral**
- The seed file doubles as local development data, so `seed` produces realistic inventory immediately.

## Links

- [[../PRD]] §8 · [[../sad]] §11 D2 · [[../../../ARCHITECTURE-OPEN-DECISIONS|O1]]
