# T01 — Domain: Profile, CvVersion, Match, Score

**Layer:** domain · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

`Profile`, `CvVersion`, `Match`, `Score` and `ScoreComponents`. Two construction guards
carry invariants: a `Match` cannot exist without at least one reason, and a `Score` cannot be
constructed without components that reconcile to its total.

## Done when

- Constructing a `Match` with no reasons throws (AC-02).
- Constructing a `Score` whose components do not reconcile to its total throws (AC-03, QG-1).
- `CvVersion` is immutable after construction; there is no setter for extracted text.
- `InterviewProbability` is a four-value enum, not a number (SAD §11 D4).
- The aggregates have no dependency on EF Core, Anthropic or Wolverine.

## Links

[[../data-model]] · [[../../../CONTEXT]] §1
