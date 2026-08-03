# T18 — Soften the seniority-floor pre-match rule for early-stage/founding roles

**Layer:** app · **Deps:** T12 · **Est:** S · **Owner:** Viacheslav

## What

The pre-match seniority floor ("two or more levels below") combined with erratic startup levelling
risks dropping Founding-Engineer / early-startup roles the Owner explicitly wants. Soften the rule:
exempt `CompanyStage ∈ {Seed, SeriesA}` from the seniority-floor exclusion, or require an explicit
parsed down-level rather than an absolute two-level gap. This protects recall for exactly the
trajectory the alignment work (TUNE-01/05) is meant to surface.

## Done when

- A Founding-Engineer / early-stage role that would previously be excluded by the seniority floor is
  no longer pre-match filtered when `CompanyStage ∈ {Seed, SeriesA}` (or when no explicit down-level is
  parsed).
- The exemption is config-driven and validated at startup.
- The existing pre-match filter behaviour for non-early-stage roles is unchanged — asserted.
- A regression case covers the previously-dropped founding role now reaching matching.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-13 ·
[[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]]
