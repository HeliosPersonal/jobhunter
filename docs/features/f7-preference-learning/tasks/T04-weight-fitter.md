# T04 — WeightFitter

**Layer:** app · **Deps:** T01 · **Est:** L · **Owner:** Viacheslav

## What

The pure fitting function: recency-weighted positive rate per dimension value, evidence
floor, bounding, normalisation, and attachment of supporting signal ids. Pure and dependency-free,
which is what makes the synthetic-behaviour corpus a fast unit test rather than an integration test.

## Done when

- All nine synthetic profiles pass, **including the indifferent one that must produce no weights at all**.
- Recency decay uses a 60-day half-life within a 180-day window, asserted by generating signals at known ages.
- No dimension exceeds 0.40 of the component under any adversarial distribution (AC-09, QG-3).
- A value with fewer than 3 supporting signals produces no weight (AC-03).
- Every produced weight carries its supporting signal ids and its positive rate.
- The function has no clock and no repository in its signature — the recency reference time is a parameter.
- 5 000 signals fit in under 30 s.

## Links

[[../adr/0001-transparent-frequency-weighting|ADR-F7-0001]] · [[../test-plan]] §The synthetic-behaviour corpus
