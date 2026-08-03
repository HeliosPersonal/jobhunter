# T16 — Encode the Owner's career goal in the Profile + match prompt

**Layer:** app · **Deps:** T01, T04 · **Est:** M · **Owner:** Viacheslav

## What

The Profile holds present-facts only; the Owner's career goal is encoded nowhere, and the match prompt
is told fit ≠ desirability — so the system optimises the past. Add Profile columns
`target_role_families jsonb`, `desired_ai_usage_floor text`, and optional `target_titles jsonb`, and add
a match-prompt section:

> "The candidate is deliberately targeting {target_role_families}. Reward genuine alignment to that
> trajectory even where it is a stretch; down-weight roles that would repeat their current track."

These are Profile facts, not CV text, so CV handling rules are untouched and no new leakage surface is
introduced.

## Done when

- The `profiles` table gains `target_role_families`, `desired_ai_usage_floor` and optional
  `target_titles`; the migration applies on a clean database.
- The match prompt includes the goal section; `PromptVersion` is bumped and golden fixtures updated
  (gate G10).
- The CV leakage suite stays green — goal fields are Profile facts, not CV text.
- A stretch Tier-1 role is scored more favourably given the goal than the same fit without it — asserted.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-05 ·
[[../data-model]] §profiles · [[../contracts/match-schema]] §Prompt
