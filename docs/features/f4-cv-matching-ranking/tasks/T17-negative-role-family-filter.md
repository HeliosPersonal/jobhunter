# T17 — Negative role-family filter (ML-Researcher / Data-Scientist / Prompt-Engineer / CRUD)

**Layer:** app · **Deps:** T08, T14 · **Est:** S · **Owner:** Viacheslav

## What

No explicit negative filter exists, so research-adjacent or prompt-writing roles can slip through on
fit plus AiUsage. Using the `RoleFamily` signal from F3 (TUNE-03), add a reason-logged down-weight
(default) or opt-in suppression for `roleFamily ∈ {MlResearch, DataScience, PromptEng, EnterpriseCrud}`,
recording the reason `"Not a target role family: {family}"`. The outcome is retrievable via `/hidden`
and counted in the digest footer (invariant 11) — never silently dropped.

## Done when

- A role in the negative family set receives the configured down-weight or opt-in suppression.
- The reason `"Not a target role family: {family}"` is recorded; the job is retrievable via `/hidden`
  and counted in the footer (invariant 11).
- The negative family set and default (down-weight vs suppress) are config-driven, validated at startup.
- A genuine ML-research or prompt-engineering role no longer reaches the top-10 on fit alone — asserted
  (feeds T19).

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-06 ·
[[../contracts/match-schema]] §Suppression · [[../../../CONTEXT]] invariant 11
