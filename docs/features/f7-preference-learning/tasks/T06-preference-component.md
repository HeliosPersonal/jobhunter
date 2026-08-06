# T06 — Preference component and precedence

**Layer:** app · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

The function F4's `ScoreCalculator` calls: score a job against the active model,
returning a bounded component plus its per-dimension contributions. Explicit Profile preferences
outrank inferred ones, always, and the conflict is recorded.

## Done when

- An explicit Profile preference overrides a contradicting learned weight, and the conflict is recorded (AC-05).
- With no active model the component is 0 and F4's remaining weights renormalise — asserted, not assumed.
- Disabled weights are excluded immediately (AC-06).
- Per-dimension contributions are returned so the score row can record them (QG-1).
- The component is always within [0,1] regardless of the model's contents.
- No F4 file is modified — F7 supplies a value, it does not change the formula.

## Implementation

- **C1 — the pure calculator.** `PreferenceComponentCalculator.Calculate(weights, facts, explicitStances)`
  (`src/JobHunter.Application/Preferences/`) is a static, pure function, like the `WeightFitter` whose output
  it consumes. Per job it sums the signed pulls of the non-disabled weights whose `(dimension, value)` the
  job's `JobFacts` carry, clamps the net to `[-1, +1]`, and maps it to `[0, 1]` by `(net + 1) / 2` where 0.5
  is indifference. A disabled weight is excluded up front (AC-06); an explicit Profile stance that contradicts
  a learned weight on the same `(dimension, value)` drops that weight and records a `PreferenceConflict`
  (AC-05). A job with no surviving weight and no conflict yields `null`, so F4 renormalises the preference
  weight away rather than scoring it at a neutral 0.5. The per-dimension `PreferenceContribution`s ride along
  for the score row (QG-1).
- **C2 — the real query.** `PreferenceModelQuery` (`src/JobHunter.Application/Preferences/`) implements the
  `IPreferenceModelQuery` port F4 already depends on, replacing the `NullPreferenceModelQuery` default in DI.
  It loads the active `PreferenceModel` (null → returns null, ranking renormalises), the active `Profile`'s
  explicit stances (preferred countries and accepted employment types → positive `Country`/`EmploymentType`
  stances), and each ranked job's current facts via `IJobFactsSnapshotQuery` — read fresh, never joined at
  fit time, so a closed/superseded job with no facts is simply omitted. It runs the C1 calculator per job and
  maps only the jobs the model has an opinion on into `ActivePreference(model.Id, componentByJob)`, stamping
  the model id so a refit is attributable (AC-04). Registered scoped because it composes scoped repositories.
- **No F4 file is touched.** F7 supplies a value `ScoreCalculator` already accepts; the formula is unchanged.

## Links

[[../../f4-cv-matching-ranking/tasks/T07-score-calculator|F4 T07]] · [[../sad]] §6.2
