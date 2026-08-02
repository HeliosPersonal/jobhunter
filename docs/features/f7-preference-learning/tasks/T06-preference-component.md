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

## Links

[[../../f4-cv-matching-ranking/tasks/T07-score-calculator|F4 T07]] · [[../sad]] §6.2
