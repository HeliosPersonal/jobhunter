# T08 — Job lifecycle: closure, reopening, near-duplicate grouping

**Layer:** app · **Deps:** T06 · **Est:** M · **Owner:** Viacheslav

## What

`JobLifecycleService`: a daily sweep closing jobs whose every alias has gone stale for
two cycles, reopening on reappearance (the fingerprint makes this automatic), and suspending closure
for jobs whose source is quarantined. Plus `NearDuplicateGrouper` for display grouping (AC-10).

## Done when

- A job whose every alias is stale for two cycles is closed, with the closure time set to the latest alias sighting (AC-06).
- A job with one stale and one fresh alias stays live.
- A reappearing posting reopens the same job — asserted end to end (AC-07).
- Closure is suspended for jobs whose only source is quarantined (SAD §11 D4).
- Near-duplicates are grouped for display and both remain queryable (AC-10).
- `JobClosed` is published exactly once per closure.

## Links

[[../sad]] §6.2 · [[../../../operations/runbooks|R4]]
