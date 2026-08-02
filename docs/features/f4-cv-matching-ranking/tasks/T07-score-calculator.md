# T07 — ScoreCalculator

**Layer:** app · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

The pure ranking function from [[../adr/0001-explainable-linear-scoring|ADR-F4-0001]]:
weighted match, preference and freshness components with a confidence multiplier. Static, no
dependencies, every input an explicit parameter — which is what makes determinism provable rather
than asserted.

## Done when

- Determinism holds over 10 000 generated inputs, under three cultures and with shuffled input ordering (QG-3).
- Components always reconcile to the final score within floating-point tolerance (QG-1).
- Freshness decay matches the specification: 1.00 today, ~0.37 at seven days.
- With no preference model present, the remaining weights renormalise — asserted explicitly.
- Ties break deterministically by job id.
- The function has no clock, no repository and no options object in its signature — asserted by an architecture test.

## Links

[[../adr/0001-explainable-linear-scoring|ADR-F4-0001]] · [[../contracts/match-schema]] §Ranking formula
