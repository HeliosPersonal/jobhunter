# T11 — Encode Tier-1/2/3 target titles as a reference config

**Layer:** app · **Deps:** T02 · **Est:** S · **Owner:** Viacheslav

## What

F2 T02 extracts seniority only; the Owner's Tier-1/2/3 target titles are encoded nowhere. Commit a
`title-tiers.yaml` reference config that maps the Tier-1/2/3 title lists from the review §5 onto
role-family archetypes. It is a committed, reviewable-in-a-diff reference — no scoring logic here — so
the downstream classifier and scoring become testable and reviewable in a diff.

The config is consumed later by:

- the F3 `RoleFamily` classifier (F3 T15), which classifies by the work described, not the title string;
- the F4 Profile goal fields (F4 T16, `target_role_families` / `target_titles`).

## Done when

- `title-tiers.yaml` exists as a committed resource, schema-shaped (tier → title list → role-family
  archetype) and loadable/validatable the same way the technology vocabulary is (fails naming the
  offending line).
- Every title tier in the review §5 is represented, each mapped to a role-family archetype consistent
  with the F3 `RoleFamily` enum (TUNE-03).
- The file is human-reviewable in a diff; adding or moving a title is a one-line YAML change, not code.
- A test asserts the config parses and that its role-family archetypes are a subset of the F3
  `RoleFamily` vocabulary (so the two never drift apart).

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-12 ·
[[T02-title-normalization]] · [[../../../reviews/career-alignment-review]] §5
