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

## Implementation

- **C1 — the explainability sentence (AC-03).** `WeightExplanation.Describe`
  (`src/JobHunter.Application/Preferences/`) renders one learned `PreferenceWeight` as a single plain sentence
  quoting the reaction that produced it — "You passed on 34 of the last 38 roles in the DE salary band." The
  count is derived from the *stored* `PositiveRate` and `SupportingSignalCount`, never recomputed, so the
  sentence stays stable after the fitting window has moved on; the rate fixes the direction (below half is
  "passed on", at or above is "engaged with"). It emits plain text — the API returns it verbatim, the Telegram
  layer escapes it — and lives in Application because both surfaces consume it.
- **C2 — disable a weight (AC-06).** `DisablePreferenceWeightHandler` loads the active model, finds the
  addressed weight, calls the idempotent `PreferenceWeight.Disable` (which keeps the first timestamp) and
  commits; an unknown id is a value-typed `WeightNotFound`, not an exception. The exclusion already in
  `PreferenceComponentCalculator` keeps a disabled weight out of the very next ranking, so "immediate" is a
  property of the read path. The evidence-doubles relearn gate lives with the fitter: a disabled weight is not
  relearned until its supporting evidence doubles.
- **C3 — reset the model.** `ResetPreferenceModelHandler` deactivates the active `PreferenceModel` wholesale
  (`Deactivate`, idempotent) and commits, reporting the deactivated version; **no signal is deleted**, so a
  future refit rebuilds from the same evidence and F4 falls back to the explicit-preference floor until it runs.
  Nothing active is a value-typed `NothingActive`.
- **C4 — toggle learning (AC-07).** `SetLearningEnabledHandler` reads the persisted `ILearningSwitch` and only
  writes when the requested state differs, reporting whether it actually changed; turning learning off deletes
  no signal, and the next digest states that learning is off (the F7 T07 C3a footer).
- **C5 — the `/hidden` read port (risk D3, invariant 11).** `IHiddenJobsQuery` (Domain) with a Dapper impl
  (`src/JobHunter.Infrastructure/Persistence/Queries/HiddenJobsQuery.cs`) returns the latest Run's suppressed
  jobs, best-score first, each with its non-blank reason, capped at the caller's limit. Read-only; it selects
  nothing about the CV.
- **C6 — owner-scoped surface (AC-10).** `ActiveWeightsQuery` (`src/JobHunter.Application/Preferences/`) is the
  shared "list active weights" read behind both surfaces, projecting each owned weight to a `LearnedWeight` with
  its id and C1 sentence, strongest pull first. `PreferenceEndpoints` (`src/JobHunter.Api/Endpoints/`) hosts the
  two reads (weights, hidden — `jobhunter:read`) and the three writes (disable, reset, learning —
  `jobhunter:admin`); every route declares its scope explicitly (the endpoint-convention gate), the
  scope-plus-Owner policy makes any other subject a 403 and an anonymous call a 401, and each write delegates to
  the same Application handler the bot uses — one write path. The `/hidden` Telegram command
  (`HiddenCommandHandler`, `src/JobHunter.Telegram/Commands/`) groups the suppressed jobs by reason under a bold
  header-and-count and renders each through the one shared `CardFormatter`; F7 owns this handler and F10 only
  registers it (the catalogue ownership table). The remaining preference commands (`/prefs`, `/forget`,
  `/floor`, `/cv`) belong to F10 by that same table and are not built here.

## Links

[[../sad]] §6.3 · [[../PRD]] §7
