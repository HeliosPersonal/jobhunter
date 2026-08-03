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

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-14 ·
[[T11-golden-ranking|T11 golden ranking set]] · [[../PRD]] §KPI (Precision@10)
