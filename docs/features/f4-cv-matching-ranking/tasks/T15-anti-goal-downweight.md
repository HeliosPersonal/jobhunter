# T15 — Down-weight anti-goal roles in the score

**Layer:** app · **Deps:** T14 · **Est:** S · **Owner:** Viacheslav

## What

Fit-dominant scoring promotes the Senior-.NET / CRUD / enterprise roles the Owner is deliberately
leaving, and nothing opposes this today. When `alignment` maps to an anti-goal — `AiUsage` None/Low
*and* a role family in {CRUD, traditional-enterprise} — apply a multiplicative penalty (e.g. `×0.5`),
or, opt-in, a reason-logged suppression `"Anti-goal role family: {family}"`. Either way the outcome is
always retrievable and counted in the digest footer (invariant 11) — never a silent drop.

## Done when

- A role classified anti-goal (low AiUsage + CRUD/enterprise family) receives the configured penalty
  or opt-in suppression.
- The penalty/suppression records a specific reason and is counted in the footer; suppressed jobs stay
  retrievable via `/hidden` (invariant 11).
- The behaviour is config-driven (penalty factor, opt-in suppression flag), validated at startup.
- A high-fit anti-goal role no longer out-ranks a genuine Tier-1 alignment role — asserted (feeds T19).

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-02 ·
[[../contracts/match-schema]] §Ranking formula, §Suppression · [[../../../CONTEXT]] invariant 11
