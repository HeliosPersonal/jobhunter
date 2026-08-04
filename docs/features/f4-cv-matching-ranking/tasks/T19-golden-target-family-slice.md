# T19 — Add a target-role-family slice to the golden ranking set

**Layer:** tests · **Deps:** T11, T14, T15 · **Est:** M · **Owner:** Viacheslav

## What

Alignment regressions must fail the build. Precision@10 is the KPI, so extend the golden ranking set
with a target-role-family slice: ≥10 golden cases each pairing a genuine target-family (Tier-1) role
against a high-fit anti-goal (e.g. CRUD) role, asserting the *relative* order (bands, not exact scores)
so that TUNE-01 / TUNE-02 / TUNE-05 are gated by the build (gate G10). Concretely, a stretch Tier-1
role must out-rank a perfect-fit CRUD role.

## Done when

- ≥10 new golden cases pair a target-family role against a high-fit anti-goal role.
- Each case asserts relative order by band, not exact score, so the slice is robust to weight tuning.
- The slice fails the build if a perfect-fit CRUD role out-ranks a stretch Tier-1 role.
- The existing golden ranking set and precision tracking continue to pass.

## Delivered

`tests/JobHunter.Application.Tests/Data/golden-target-family-slice.yaml` and
`GoldenTargetFamilySliceTests` add a ten-pair slice alongside the existing 50-case golden set (which is
left untouched, so it and precision tracking keep passing). Each pair couples one genuine **target**-family
(Tier-1) role that is only a *stretch* on fit against one **off-target** role the model scores much higher
on fit — five against an anti-goal enterprise-CRUD role (T15), five against the off-target family set (T17,
default `{MlResearch, DataScience, PromptEng}`).

The slice is judged by the **same pure chain** as `GoldenRankingSetTests` — `PreMatchFilter` (a deliberate
no-op: every role is Senior / FullTime / EMEA-remote / enriched / just-seen, so nothing is factually
excluded and the slice isolates alignment + career-policy), then `AlignmentCalculator`, the anti-goal /
negative-family classifiers and `ScoreCalculator` — with the default options (both penalty factors 0.50,
both suppression opt-ins off). Per pair it asserts: the off-target is the stronger *raw* fit (or the pair
proves nothing), both sides land their recorded band, and the target lands a **strictly better band and a
higher final score**. Assertions are on bands and relative order, never exact scores, so the slice survives
weight tuning and fails the build only when a re-weighting lets a high-fit off-target role out-rank a
stretch Tier-1 role — the regression TUNE-01/02/05/06 must never reintroduce.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-14 ·
[[T11-golden-ranking|T11 golden ranking set]] · [[../PRD]] §KPI (Precision@10)
