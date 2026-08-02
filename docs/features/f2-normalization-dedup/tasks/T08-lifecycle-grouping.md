# T08 — Job lifecycle: closure and reopening

**Layer:** app · **Deps:** T06 · **Est:** M · **Owner:** Viacheslav

## What

`JobLifecycleService`: a daily sweep closing jobs whose every alias has gone stale for
two cycles, reopening on reappearance (the fingerprint makes this automatic), and suspending closure
for jobs whose source is quarantined.

> **Relocated:** `NearDuplicateGrouper` (display grouping, AC-10) is **computed at digest assembly in
> F5**, per [[../adr/0001-conservative-fingerprint|ADR-F2-0001]] ("computed at digest assembly").
> It is no longer part of F2; F5's assembly step owns it. The `idx_jobs_normalised_title_trgm`
> trigram index stays on the F2-owned `jobs` table, since F5 queries it.

## Done when

- A job whose every alias is stale for two cycles is closed, with the closure time set to the latest alias sighting (AC-06).
- A job with one stale and one fresh alias stays live.
- A reappearing posting reopens the same job — asserted end to end (AC-07).
- Closure is suspended for jobs whose only source is quarantined (SAD §11 D4).
- `JobClosed` is published exactly once per closure.

## Links

[[../sad]] §6.2 · [[../../../operations/runbooks|R4]]
