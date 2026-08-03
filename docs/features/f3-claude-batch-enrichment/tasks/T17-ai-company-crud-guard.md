# T17 — Sharpen the "AI company, CRUD work" guard into an acted-on signal

**Layer:** claude · **Deps:** T15 · **Est:** S · **Owner:** Viacheslav

## What

The `AiUsage = Low` definition ("a company that sells an AI product but whose posting describes CRUD
work is Low") is correct but inert, because AiUsage is not scored today. This task makes the guard
concrete on the enrichment side: ensure the combination of low AiUsage / `usesAiTooling` with a
non-target `RoleFamily` (e.g. `EnterpriseCrud`) is emitted clearly and with a reason, so that once the
F4 `alignment` component (TUNE-01) lands, a low-AiUsage role at an AI-brand company scores by alignment
rather than by company prestige.

## Done when

- The prompt and schema make the "AI-brand company, CRUD work" case unambiguous: low AiUsage plus a
  non-target `RoleFamily`, each with a specific reason.
- A golden fixture asserts that an AI-brand posting describing CRUD work produces low AiUsage and a
  non-target role family (not a prestige-driven high signal).
- `PromptVersion` is bumped and golden fixtures are updated where wording changes (gate G10).

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-11 ·
[[../contracts/enrichment-schema]] §Prompt · [[../../f4-cv-matching-ranking/contracts/match-schema]]
§Ranking formula
