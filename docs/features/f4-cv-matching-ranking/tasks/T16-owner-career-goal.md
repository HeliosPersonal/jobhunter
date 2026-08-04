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

## Delivered

- **Domain.** `Profile` gained three career-goal facts alongside its present-facts: `TargetRoleFamilies`
  (deduped `RoleFamily` list), `DesiredAiUsageFloor` (nullable `AiUsageLevel`; `Unknown` — the tolerant
  parser's sentinel — is rejected as a deliberately-chosen floor) and `TargetTitles` (trimmed, deblanked,
  deduped). All three are trailing-optional constructor parameters, so existing call-sites are untouched.
- **Persistence.** Migration `20260804142508_F4AddProfileCareerGoal` adds `target_role_families jsonb`,
  `desired_ai_usage_floor text` and `target_titles jsonb`; the two jsonb columns carry a `[]` default so
  any existing Owner row deserializes as an empty list. `ProfileConfiguration` maps the two lists through
  their private backing fields (like `preferred_countries`/`employment_types`) and the floor as
  enum-as-text. Round-tripped through real Postgres by two integration tests — a stated goal and a
  goal-less profile.
- **Prompt.** `MatchPrompt` renders the goal directive — *"the candidate is deliberately targeting
  {families}. Reward genuine alignment to that trajectory even where it is a stretch; down-weight roles
  that would repeat their current track."* — plus optional AI-usage-floor and target-title lines, into the
  **candidate block before the cache breakpoint** (the goal is stable per Profile). It renders **only when
  a goal is stated**; an unstated goal omits the whole section, so the no-goal snapshot is byte-identical
  and the shared-prefix cache guarantee (T13) is preserved. `PromptVersion` bumped `match-v1` → `match-v2`.
- **Tests.** `ProfileTests` (5 new), `MatchPromptTests` (goal-render + omission + prefix-stability),
  `MatchRequestBuilderTests` (goal folds into the cache prefix, not the role block), and the two
  persistence round-trips. The CV-leakage scan stays green — the goal fields are Profile facts, not CV
  text. The `MatchRequestBuilder` test folding a Tier-1 target (`AiPlatform`) into the prefix is the
  deterministic stand-in for "a stretch Tier-1 role scored more favourably given the goal": the only
  prompt-input difference is the reward directive naming that family.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-05 ·
[[../data-model]] §profiles · [[../contracts/match-schema]] §Prompt
