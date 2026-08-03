# T15 — Emit a RoleFamily / title-tier classification

**Layer:** claude · **Deps:** T02, T08 · **Est:** M · **Owner:** Viacheslav

## What

Add a `roleFamily` enum to `EnrichmentOutput` so downstream scoring has a structured signal for the
Owner's Tier-1/2/3 target roles. Values:
`AiPlatform | Platform | AiApplications | ForwardDeployed | FoundingEng | BackendGeneric | Frontend |
Fullstack | DevOpsSRE | MlResearch | DataScience | PromptEng | EnterpriseCrud | Other`, each carrying
at least one reason (invariant 4). The prompt guidance must classify by *the work described in the
posting*, not by the title string.

This is the enrichment-side signal that the F4 `alignment` component (TUNE-01) and the F7 preference
dimensions (TUNE-08) act on; without it there is no structured notion of the target trajectory.

## Done when

- `EnrichmentOutput` carries a `RoleFamily` enum with the values above and an `Other` fallback for
  unrecognised work.
- The generated JSON Schema constrains `roleFamily` to the closed enum; `PromptVersion` is bumped and
  golden fixtures are updated (gate G10).
- The prompt instructs the model to classify by the described work, not the title, with a specific
  reason quoting or paraphrasing the posting.
- A posting whose title says "AI Engineer" but whose work is CRUD classifies as `EnterpriseCrud`, not
  `AiPlatform` — covered by a golden fixture.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-03 ·
[[../contracts/enrichment-schema]] §Output record · [[../../../CONTEXT]] invariant 4
