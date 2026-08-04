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

## Scope note

This task, as originally written, bundled two separable things: the **golden ranking set + gate G10**
(the deterministic quality gate, above) and the **weekly `precision@10` rating loop** (a Telegram
prompt asking the Owner to rate last week's cards, recorded as F7 Signals and exported as
`jobhunter.precision_at_10`). The rating loop needs infrastructure that does not exist yet — the
`signals` table ([[../../../f7-preference-learning/tasks/T03-signal-capture|F7 T03]]), the digest that
shows the cards to rate ([[../../../f5-daily-digest-delivery/_epic|F5]]) and the Telegram command
surface ([[../../../f10-telegram-command-surface/_epic|F10]]) — so it is split into
[[T20-precision-at-10-loop|T20]] rather than stubbed against absent tables. The golden set does not
depend on any of that and ships here, now.

## Done when (moved to [[T20-precision-at-10-loop|T20]])

- The weekly rating prompt records Signals usable by F7 as well as by the metric.
- `jobhunter.precision_at_10` is exported and charted.
- A baseline is captured at M4 so later improvement is measurable rather than asserted.

## Links

[[../test-plan]] §The golden ranking set · [[T20-precision-at-10-loop|T20]] · [[../../../DECISION-LOG|D5]]
