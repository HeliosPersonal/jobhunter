# T11 — Telemetry primitives and correlation

**Layer:** platform · **Deps:** T03, T08 · **Est:** M · **Owner:** Viacheslav

## What

`Application/Common/Telemetry.cs` with the single `ActivitySource` and `Meter` and the
core eight domain instruments from [[../../../engineering/observability|observability]] §2 (later
features add nine feature-specific instruments to the same file, for seventeen in total). A Wolverine
middleware that opens a span and a logging scope carrying the correlation id for every message, so
no handler has to remember to do it. A log-scrubbing processor as the second line of defence for
secrets.

## Done when

- A unit of work spanning two handlers produces one end-to-end trace and correlated logs (AC-05).
- Handlers do not open scopes themselves — the middleware does it.
- Metric labels are restricted to the allowed set; a test asserts no instrument accepts an id-shaped label.
- The scrubbing processor redacts known secret patterns from log messages and span attributes.
- Telemetry failure never propagates into business code (AC-06).

## Links

[[../../../engineering/observability]] §2 · [[../sad]] §8
