# T07 — JSON-LD career-page adapter (Tier 2)

**Layer:** scrapers · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

The reference Tier-2 adapter: fetch a careers page, extract
`<script type="application/ld+json">` blocks, keep those with `@type == "JobPosting"`, map
`schema.org` fields. Confidence is capped at 0.70 because page structure varies wildly.

## Done when

- Multiple JSON-LD blocks on one page are all considered; non-JobPosting types are ignored.
- A missing `identifier` synthesises one by hashing the apply URL.
- `@graph`-wrapped and array-wrapped JSON-LD are both handled.
- Malformed JSON in one block does not prevent parsing the others.
- Tier-2 postings are marked so ranking can down-weight or exclude them.

## Out of scope

- HTML scraping without JSON-LD — explicitly out of scope ([[../../../00-overview/adr/0009-ats-first-no-linkedin|ADR-0009]]).

## Links

[[../contracts/ats-endpoints|ATS endpoints]] §Career pages
