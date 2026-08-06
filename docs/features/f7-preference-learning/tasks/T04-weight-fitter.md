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

## Implementation

Three commits, each its own RED→GREEN cycle:

- **C1 — fitter core** (`WeightFitter.cs`, `SignalFact`/`FittingOptions`/`FittedWeight`/`FittedModel`).
  `Fit(signals, options)` is pure — no clock, no repository; `options.ReferenceTime` is the "now" recency
  decays from. It keeps signals inside the 180-day `Window` (their count is `FittedModel.SignalCount`),
  groups them by the `(dimension, value)` pairs each signal's `JobFacts` carries, and per group accumulates
  a recency-weighted positive rate: each signal's stored evidence `Weight` scaled by
  `0.5 ^ (ageDays / 60)`, positives over total. A value below `PreferenceWeight.MinSupportingSignals` (3)
  distinct signals is dropped (AC-03); a rate within `IndifferenceBand` (±0.05) of 0.5 is dropped (the
  indifferent Owner earns nothing); the surviving rate maps to a signed weight `2·rate − 1 ∈ [−1, +1]`.
  Polarity: `Ignored`/`Rejected` are negative, all other kinds positive (the ADR's prose omits
  `Opened`/`Rated` — both are the Owner leaning in, so both count positive).

- **C2 — bounding & normalisation** (`DimensionBounding.cs`). Each dimension's total absolute weight is its
  contribution mass; masses are normalised across dimensions and water-filled so none exceeds
  `MaxDimensionShare` (0.40) — surplus over the cap is redistributed to dimensions with headroom, repeating
  until stable. The cap is the hard invariant and always holds; the masses sum to one only when they *can*
  (≥ 3 weighted dimensions, since 2 × 0.40 < 1). Sign is always preserved — bounding scales influence, it
  never flips a preference. Asserted over a family of adversarial distributions (AC-09, QG-3).

- **C3 — synthetic-behaviour corpus** (`SyntheticOwnerGenerator.cs`, `SyntheticCorpusTests.cs`). Nine
  seeded profiles, each a different failure mode; the indifferent profile emits every job twice (saved and
  ignored, same facts and age) so every value's rate is 0.5 by construction and **no weight is produced** —
  the case that separates learning from superstition. Also the 5 000-signal < 30 s ceiling.

The fitter has **no activation floor** — the 200-signal `ActivationThreshold` is the learner's decision
(T05), not the fitter's. The fitter fits whatever evidence it is given and reports the count.

## Links

[[../adr/0001-transparent-frequency-weighting|ADR-F7-0001]] · [[../test-plan]] §The synthetic-behaviour corpus
