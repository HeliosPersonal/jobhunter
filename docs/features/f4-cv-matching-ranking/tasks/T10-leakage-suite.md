# T10 — CV leakage scan suite

**Layer:** tests · **Deps:** T06, T08 · **Est:** L · **Owner:** Viacheslav

## What

The feature's security gate (QG-2). Seed a CV with twelve unique sentinel tokens, run a
complete pipeline including digest delivery and search indexing, then scan every emitted artifact —
logs, span attributes, Typesense documents, Telegram messages, API responses, stored raw results —
for any occurrence.

## Done when

- Zero sentinel occurrences across every collected artifact (AC-06).
- The suite also runs at `Debug` log level — a leak that only appears during investigation is the worst kind.
- Forced-failure cases assert exception messages and stack traces carry no sentinel.
- A deliberately introduced leak is detected — the suite is proven able to fail.
- The scan covers `batch_items.raw_result` as well as live telemetry.
- The suite runs on every PR, with no allowlist and no sampling.

## Links

[[../test-plan]] §The leakage suite · [[../../../engineering/security]] §1
