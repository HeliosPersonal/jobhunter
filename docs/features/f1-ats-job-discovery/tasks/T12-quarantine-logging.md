# T12 — Quarantine, fetch logging and degraded reporting

**Layer:** app · **Deps:** T11 · **Est:** M · **Owner:** Viacheslav

## What

The failure path: count consecutive failures, quarantine for 24 h at two, notify once,
publish `SourceQuarantined`, and record every attempt — successful or not — in `source_fetch_log`.
Plus the degraded-coverage summary the digest footer consumes (AC-09).

## Done when

- Two consecutive failures quarantine the source, notify once, and leave other sources untouched (AC-08).
- A success resets the failure counter to zero.
- Quarantine expiry is picked up on the next cycle, not retried immediately.
- Every attempt produces exactly one log row, including transport failures with status 0 (AC-11).
- A degraded-coverage summary is queryable for the digest (AC-09).
- The notification fires once per quarantine event, not once per cycle.

## Links

[[../sad]] §6.3 · [[../../../operations/runbooks|R4]]
