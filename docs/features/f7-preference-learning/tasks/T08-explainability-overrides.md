# T08 — Explainability view and Owner overrides

**Layer:** app/api · **Deps:** T07 · **Est:** M · **Owner:** Viacheslav

## What

The endpoints and bot commands that make QG-1 usable: show a weight with its evidence in
one sentence, disable it, reset the model, and turn learning off entirely. All owner-scoped.

## Done when

- Every weight renders as one sentence quoting its rate and count — for example, 34 of 38 ignores below a threshold (AC-03).
- Disabling a weight takes effect on the next ranking and is not relearned until its evidence doubles (AC-06).
- A full reset deactivates the model without deleting any signal.
- Turning learning off applies only explicit preferences and the digest states it (AC-07).
- All operations are owner-scoped; without the scope they are refused (AC-10).
- A `/hidden` view lists suppressed jobs with their reasons, so suppression regret is measurable.

## Links

[[../sad]] §6.3 · [[../PRD]] §7
