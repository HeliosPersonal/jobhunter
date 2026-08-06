# T01 — Domain: Application, TransitionRules, ReminderPolicy

**Layer:** domain · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

The `Application` aggregate, `ApplicationStatus`, `StatusTransition`, and `TransitionRules`
as a **table** rather than a chain of conditionals — a table can be enumerated by a test, which is how
all 49 status pairs get covered rather than the handful someone thought of.

## Done when

- `TransitionRules.Evaluate` matches [[../contracts/application-api|the contract]] table for every pair.
- Every refusal carries a remedy message, not just a rejection.
- `applied_at` is set on first entry to `Applied` and never changed afterwards.
- `Application.MarkPostingClosed()` does not alter the status (AC-07); it records the closure as a
  `System` self-transition (refined in T05) rather than fabricating a move.
- `ReminderPolicy` maps status to threshold from configuration, with no hard-coded durations.

## Links

[[../adr/0001-permissive-transitions-with-history|ADR-F6-0001]] · [[../contracts/application-api]]
