# T05 — PreferenceLearner and weekly refit

**Layer:** app · **Deps:** T04, T02 · **Est:** M · **Owner:** Viacheslav

## What

The Monday 03:00 job: load the window, check the 200-signal threshold, fit, insert a new
model version, and flip activation atomically so a bad refit is a rollback rather than an incident.

## Done when

- With ≥ 200 signals, a new version is fitted and activated atomically (AC-01).
- With fewer, no model is produced and the reason is recorded on the previous model's notes (AC-02).
- The threshold boundary is asserted at 199 and 200.
- Activation is atomic — a concurrent ranking sees exactly one model, old or new.
- The previous model remains queryable, so rollback is a flag change.
- `PreferenceModelUpdated` is published on activation.
- The schedule survives a restart and is asserted across a DST week.

## Implementation

Three commits, each its own RED→GREEN cycle:

- **C1 — the signal window read port** (`ISignalWindowQuery` in Application, `SignalWindowQuery` in
  Infrastructure). `LoadSince(occurredFrom)` returns the signals whose `occurred_at ≥` the cutoff, newest
  first, each rehydrated into a `SignalFact` (kind, evidence weight, snapshotted `JobFacts`, instant). It
  reads the `signals` table directly with Dapper and rehydrates the `job_facts` jsonb — **never a join to
  `jobs`** — so a later edit to a posting cannot rewrite the facts the Owner actually reacted to (T03). Read
  only; Dapper never writes.

- **C2 — the learner** (`PreferenceLearner`, `RecomputePreferencesDue`, `PreferenceModelUpdated`). The
  Wolverine handler computes `cutoff = FittedAt − Window`, loads the window, runs the pure `WeightFitter`,
  and takes `version = LatestVersion + 1`. With `SignalCount ≥ ActivationThreshold` (200) it maps the fitted
  weights to `PreferenceWeight`s, deactivates the prior active model and activates the new one, publishes
  `PreferenceModelUpdated`, and commits **once** — so `uq_preference_models_active` is never momentarily
  violated and the flip and the outbox publish are atomic (AC-01, done-when 4). Below the floor it writes a
  new **inactive** version whose `notes` carry the reason (`"insufficient evidence: N signals"`) and leaves
  the prior active model untouched — models are immutable, so "recorded on the previous model's notes" is a
  new inactive version rather than an in-place edit (AC-02). The 199/200 boundary is asserted directly.

- **C3 — the weekly schedule and wiring** (`PreferenceRefitTrigger`, DI). A thin Hangfire body stamps the
  refit instant from `IClock` and publishes one `RecomputePreferencesDue`; a `RecurringJobBinding` installs
  it on the Monday 03:00 Europe/Kyiv cron (`0 3 * * 1`) through F0's `RecurringJobApplier`, so the schedule
  is re-declared on every Worker start and survives a restart (done-when 7). The cron is declared in
  `Europe/Kyiv`, not UTC: both 2026 DST transitions fall on the Sunday, so the Monday-after 03:00 slot is
  never inside the gap or overlap and stays a stable wall-clock across a DST week (done-when 7).

The atomic single-commit and the exactly-one-active flip are asserted in Application unit tests over an
in-memory repository (`SaveCount == 1`, one active version at v2 with the prior still queryable); the
zero-network signal-window projection is asserted in Infrastructure integration tests against
`postgres:17-alpine`.

## Links

[[../sad]] §6.1 · [[../../f0-platform-foundation/tasks/T09-hangfire|F0 T09]]
