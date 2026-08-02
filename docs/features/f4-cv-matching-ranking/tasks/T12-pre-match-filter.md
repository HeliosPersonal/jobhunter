# T12 — Pre-match filter

**Layer:** app · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

`PreMatchFilter`, applied after enrichment and before the matching batch is built. It excludes a job
from deep-tier matching only on the **factual** disqualifiers listed in
[[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]] — timezone, employment type, seniority
floor, high-confidence salary floor, lifecycle. Every exclusion writes a `scores` row with
`suppressed = true` and a reason naming the rule, so the job stays retrievable and is counted in the
digest footer.

The line this task must hold: a rule is a **fact drawn from `enrichments` and the `Profile`**, never
a judgement. "Below the stated salary floor at high confidence" is a fact. "Probably a weak culture
fit" is a judgement and belongs in the deep tier.

## Done when

- All five rules from [[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]] are implemented, each independently unit-tested at its boundary.
- Pass rate lands in the 35–50% band on the reference corpus; the test fails outside it in either direction.
- Every excluded job has a `scores` row with `suppressed = true` and a reason naming the specific rule (AC-12, invariant 11).
- Excluded jobs are counted in the digest footer and listed by `/hidden` — asserted end to end.
- `Run:MatchAllJobs = true` bypasses the filter entirely, matching everything (AC-13).
- Matching cost at 150 discovered jobs stays under $0.60, asserted against `PricingTable`.
- The filter reads only `enrichments` and `Profile` — an architecture test forbids it referencing `matches`, `scores` or CV text.

## Out of scope

- Learned preference suppression — F7 owns that, and its rules stay separable from these.
- Any judgement-based exclusion. If it needs the CV to decide, it goes to the deep tier.

## Links

[[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]] · [[../PRD]] AC-12, AC-13 · [[../../../CONTEXT]] invariant 11
