# T11 — Batch poller with backoff and deadline

**Layer:** app · **Deps:** T10 · **Est:** M · **Owner:** Viacheslav

## What

A delayed Hangfire job that re-enqueues itself rather than looping — which is what makes
backoff survive a restart. Schedule 2 min doubling to 15 min, jittered, with a 6 h cap. At 06:45 the
deadline check ships what completed and carries the rest over; **07:00 is never delayed**.

## Done when

- The backoff schedule matches the specification, asserted against `FakeClock` with no real waiting.
- Poll attempts are recorded, so a flat counter is diagnosable as a stalled poller ([[../../../operations/runbooks|R2]]).
- A restart mid-poll resumes polling the same provider batch and never resubmits (AC-05).
- At the deadline, an incomplete batch ships partial and records the carry-over count (AC-09).
- The 6 h cap marks the batch failed and carries its items to the next Run.
- Jitter prevents several batches polling in lockstep.

## Links

[[../sad]] §6.2, §6.3
