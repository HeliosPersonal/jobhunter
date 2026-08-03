# T14 — Add an `alignment` score component

**Layer:** app · **Deps:** T07 · **Est:** M · **Owner:** Viacheslav

## What

The current formula `100 × (0.60·match + 0.25·preference + 0.15·freshness) × confidence` has no term
that rewards the Owner's AI-platform / platform / staff trajectory, so fit-to-CV buries aspiration.
Add a fifth, explainable component `alignment ∈ [0,1]` and re-weight:

`final = 100 × (0.45·match + 0.20·alignment + 0.20·preference + 0.15·freshness) × confidence`.

`alignment` is a monotone function of `AiUsage` (None=0, Low=0.25, Medium=0.6, High=1.0) blended with
role-family tier (Tier1=1.0, Tier2=0.7, Tier3=0.4, anti-goal=0.0), using the `RoleFamily` signal from
F3 (TUNE-03). It is persisted as a stored component like the others so QG-1 reconciliation still holds.
ADR-F4-0001 explicitly allows adding score components, so this is a tuning change, not a rearchitecture.

## Done when

- `ScoreCalculator` takes `alignment` as an explicit parameter and the re-weighted formula reconciles
  to the final score within floating-point tolerance (QG-1).
- Determinism still holds over generated inputs, cultures and shuffled ordering (QG-3).
- `alignment` is persisted as a stored component alongside match/preference/freshness.
- A role with `AiUsage = None` and an anti-goal family contributes 0 alignment; a Tier-1 high-AiUsage
  role contributes 1.0 — asserted.
- The component carries at least one reason (invariant 4).

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-01 ·
[[../adr/0001-explainable-linear-scoring|ADR-F4-0001]] · [[../contracts/match-schema]] §Ranking formula
