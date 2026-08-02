# T06 — CostAccountant, pricing table and token estimation

**Layer:** claude · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

`CostAccountant` and `PricingTable`. Estimation counts tokens from the **actually
rendered prompt** rather than a heuristic, which is the reason the estimate lands within 20% instead
of within a factor. Output tokens are estimated pessimistically from the schema's maximum plausible
size, so the ceiling errs toward under-spending.

## Done when

- Estimates land within 20% of reported actuals across the whole fixture corpus.
- The batch discount is applied; a test asserts the discounted figure, not the list price.
- A missing tier in the pricing table fails startup — an unpriced tier makes the ceiling meaningless.
- Cheap and deep tiers configured with the same model id fails startup (a silent cost doubling).
- Cost arithmetic uses `decimal` throughout; a property test asserts no floating-point drift over 10 000 entries.

## Links

[[../contracts/enrichment-schema|contract]] §Cost model · [[../adr/0002-pre-submission-cost-ceiling|ADR-F3-0002]]
