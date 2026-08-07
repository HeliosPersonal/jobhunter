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

## Implementation

- **C0 — the nine-profile corpus (done-when 1, 2, 6).** `SyntheticCorpusTests`
  (`tests/JobHunter.Application.Tests/Preferences/`) drives the fitter with nine seeded synthetic profiles,
  each reproducible from its `[InlineData]` seed, and asserts the weights each should learn. The indifferent
  profile — signals spread evenly across every value of a dimension — must produce **no** weight, the test
  that separates learning from superstition. The suite is the regression gate: any change to the fitting
  method has to keep all nine green.
- **C1 — the card-floor property suite (done-when 3).** `CardFloorPropertyTests`
  (`tests/JobHunter.Application.Tests/Reporting/`) is the display-time counterpart to the fitter's dimension
  bound (`WeightFitterBoundingTests`, already covering the bound half of done-when 3). Over eight seeded
  adversarial score distributions it asserts the digest is never emptied below the floor while reasoned
  candidates remain, never exceeds the cap, and that the suppressed count still reconciles to the raw
  suppressed rows (invariant 11) — restoration is display-only, never a re-score.
- **C2 — precision@10 (done-when 4, AC-08).** `IPrecisionAtTenQuery` (Domain) with a Dapper impl
  (`src/JobHunter.Infrastructure/Persistence/Queries/PrecisionAtTenQuery.cs`) projects, per Run, the shown
  (never suppressed) top-ten scores joined to the positive signals on those jobs, returning a
  `PrecisionAtTenPoint` series oldest Run first. Each point is bucketed `after_activation` on whether the
  Run's scores carried a `preference_model_id`, so the before-and-after halves are directly comparable and
  "was any of this worth building?" is answerable from recorded data alone. Read-only; it selects nothing
  about the CV.
- **C3 — suppression regret (done-when 5, risk D3).** `ISuppressionRegretQuery` (Domain) with a Dapper impl
  (`src/JobHunter.Infrastructure/Persistence/Queries/SuppressionRegretQuery.cs`) counts the latest Run's
  suppressed jobs that carry a positive signal — retrieved through `/hidden` and opened, saved or applied to.
  `SuppressionRegretReporter` (`src/JobHunter.Application/Preferences/`) records that count as the
  `jobhunter.preferences.suppression_regret` gauge on the shared meter — the counterweight to precision@10, so
  over-suppression is visible rather than silent (invariant 11).

## Links

[[../test-plan]] · [[../../../DECISION-LOG|D5]]
