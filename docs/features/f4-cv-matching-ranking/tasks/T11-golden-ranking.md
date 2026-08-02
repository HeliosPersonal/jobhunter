# T11 — Golden ranking set and precision tracking

**Layer:** tests · **Deps:** T08 · **Est:** L · **Owner:** Viacheslav

## What

The 50-job golden set with expected score bands and top-5 relative ordering, plus the
weekly `precision@10` measurement: a Telegram prompt asking the Owner to rate the previous week's top
cards, recorded as Signals and exported as a metric.

## Done when

- All ten difficult cases from [[../test-plan|test-plan]] §The golden ranking set are covered.
- Assertions are on bands and relative order, never exact scores — no flaky test.
- A change to the prompt, schema or weights that breaks the set fails the build (gate G10).
- The weekly rating prompt records Signals usable by F7 as well as by the metric.
- `jobhunter.precision_at_10` is exported and charted.
- A baseline is captured at M4 so later improvement is measurable rather than asserted.

## Links

[[../test-plan]] §The golden ranking set · [[../../../DECISION-LOG|D5]]
