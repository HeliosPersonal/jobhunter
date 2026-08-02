# T09 — Operations commands

**Layer:** telegram · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

`/status`, `/cost`, `/sources`, `/run`, `/redeliver` — the chat replacement for the most
common runbook steps, so recovery does not need a terminal.

## Done when

- `/status` reports outcome, cost against ceiling, counts and degraded sources (AC-06) — [[../../../operations/runbooks|R1]]'s first question.
- `/cost` breaks spend down by stage and tier and flags estimate-vs-actual drift above 20%.
- `/sources` lists per-provider health with a release button for quarantined sources ([[../../../operations/runbooks|R4]]).
- `/run` is refused with an explanation when a Run is already live; the confirmation names the estimated cost.
- `/redeliver` states how many cards would actually be sent — usually zero, which is the point.
- All five are `Operator`-scoped and all three state-changing ones require confirmation.
- The runbooks are updated to reference these commands alongside the API endpoints.

## Links

[[../contracts/command-catalogue|catalogue]] §Operations · [[../../../operations/runbooks]]
