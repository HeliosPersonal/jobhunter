# T02 — Domain: Enrichment and its value objects

**Layer:** domain · **Deps:** — · **Est:** S · **Owner:** Viacheslav

## What

`Enrichment`, `SalaryEstimate`, `TimezoneBand`, `AiUsageLevel`, `CompanyStage`. The
construction guard is the interesting part: an enrichment cannot be constructed without at least one
reason, so [[../../../CONTEXT]] invariant 4 is a type-level property rather than a validation step
someone can forget.

## Done when

- Constructing an `Enrichment` with an empty or whitespace-only reasons list throws (AC-02).
- `SalaryEstimate` carries confidence and refuses cross-currency comparison.
- Every enum has an `Unknown` member so an unrecognised provider value has somewhere to go.
- `Enrichment` is immutable; a correction is a new row for a new Run.

## Links

[[../data-model]] §enrichments · [[../../../CONTEXT]] invariant 4
