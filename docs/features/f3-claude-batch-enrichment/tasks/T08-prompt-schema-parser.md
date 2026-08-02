# T08 — Enrichment prompt, schema and tolerant parser

**Layer:** claude · **Deps:** T02 · **Est:** L · **Owner:** Viacheslav

## What

`EnrichmentPrompt` (versioned raw strings), the JSON Schema generated from
`EnrichmentOutput` so the two cannot drift, and `TolerantJsonParser` implementing the eight parsing
steps in [[../contracts/enrichment-schema|the contract]]. Step 8 is the one that matters at 03:00: an
unrecognised enum value degrades to `Unknown` and never throws.

## Done when

- All eleven fixtures in [[../test-plan|test-plan]] §Fixture corpus pass.
- An unknown enum value degrades to `Unknown` and is logged — it never throws.
- An inverted salary range is swapped; an unknown currency drops the salary and keeps the rest.
- Confidence outside [0,1] is clamped rather than rejected.
- An empty reasons array is rejected as a parse failure even though the schema forbids it (AC-02).
- The prompt is a pure function of its inputs; its rendering is snapshot-tested so a change is visible in a diff.
- Description truncation happens at a paragraph boundary and is recorded on the batch item.

## Links

[[../contracts/enrichment-schema|contract]] · [[../../../00-overview/adr/0006-structured-output-contract|ADR-0006]]
