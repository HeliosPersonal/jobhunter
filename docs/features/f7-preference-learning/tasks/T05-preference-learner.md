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

## Links

[[../sad]] §6.1 · [[../../f0-platform-foundation/tasks/T09-hangfire|F0 T09]]
