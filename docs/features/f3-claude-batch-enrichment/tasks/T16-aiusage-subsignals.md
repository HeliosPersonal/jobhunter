# T16 — Refine AiUsage resolution with sub-signals

**Layer:** claude · **Deps:** T08 · **Est:** M · **Owner:** Viacheslav

## What

The single 4-value `AiUsage` scalar cannot separate AI-platform engineering from ML-research from
"the team uses Copilot." Add resolving sub-signals alongside the existing scalar — booleans/enums such
as `buildsAiProduct`, `buildsAiInfra`, `usesAiTooling`, `isResearch` — each framed by the "engineering
work" language already in the enrichment prompt. This sharpens the target/trap boundary the review
flags, so a role that merely *uses* AI tooling is not confused with one that *builds on or with* AI.

## Done when

- `EnrichmentOutput` carries the new sub-signals next to `AiUsage`; the existing scalar is kept.
- The generated JSON Schema is updated, `PromptVersion` is bumped, and golden fixtures are refreshed
  (gate G10).
- The prompt derives each sub-signal from the described engineering work, with a specific reason.
- A posting that sells an AI product but describes CRUD work resolves `usesAiTooling`/low signals, not
  `buildsAiProduct` — covered by a golden fixture.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-04 ·
[[../contracts/enrichment-schema]] §Output record · [[../../../CONTEXT]] invariant 4
