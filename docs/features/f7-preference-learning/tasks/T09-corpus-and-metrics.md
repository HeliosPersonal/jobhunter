# T09 — Synthetic corpus, property suite and precision tracking

**Layer:** tests · **Deps:** T04, T07 · **Est:** L · **Owner:** Viacheslav

## What

The nine-profile synthetic corpus, the adversarial property suite, and the
before-and-after `precision@10` comparison that answers whether any of this was worth building.

## Done when

- All nine profiles pass, each with a seeded RNG so a failure is reproducible from its seed.
- **The indifferent profile produces no weights** — the test that separates learning from superstition.
- The property suite asserts the dimension bound and the card floor over generated adversarial distributions.
- A `precision@10` series before and after activation is queryable from recorded data (AC-08).
- Suppression regret — retrieved-then-acted-on suppressed jobs — is exported as a metric.
- The corpus is the regression suite: any change to the fitting method must keep all nine passing.

## Links

[[../test-plan]] · [[../../../DECISION-LOG|D5]]
