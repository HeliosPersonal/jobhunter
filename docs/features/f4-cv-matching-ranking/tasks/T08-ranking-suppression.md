# T08 — Ranking handler and suppression

**Layer:** app · **Deps:** T07, T06 · **Est:** M · **Owner:** Viacheslav

## What

`RankingHandler` consuming `MatchingCompleted`: load matches, enrichments and the active
preference model, score every job, evaluate suppression, persist every component, and publish
`RankingCompleted` with the counts the digest footer needs.

## Done when

- Every non-suppressed job has exactly one score for the Run (AC-11).
- Every suppression records a reason and the job remains retrievable (AC-05, invariant 11).
- The preference model id is stamped on each score, so a bad refit is attributable (AC-04).
- All jobs suppressed still produces a reportable result — never an empty response indistinguishable from a failure.
- Ranking 500 jobs completes in under 5 s.
- Re-running ranking for a Run produces identical scores.

## Links

[[../sad]] §6.2 · [[../../../CONTEXT]] invariant 11
