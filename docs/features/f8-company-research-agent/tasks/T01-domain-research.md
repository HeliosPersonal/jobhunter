# T01 — Domain: CompanyResearch, ResearchClaim, categories

**Layer:** domain · **Deps:** — · **Est:** S · **Owner:** Viacheslav

## What

`CompanyResearch`, `ResearchClaim`, `ResearchCategory`, `ResearchSource` and the freshness
policy. The construction guard carries the invariant: a claim cannot be constructed without a source,
so an uncited claim is unrepresentable rather than merely rejected.

## Done when

- Constructing a `ResearchClaim` without a source throws (AC-02, invariant 5).
- The eight categories are a closed enum; `News` and `Layoffs` carry the shorter freshness threshold.
- `Freshness.IsStale(generatedAt, category, now)` is a pure function driven by an injected clock.
- A claim's observed date is copied from its source, never set independently (AC-03).
- The aggregate has no dependency on HTTP, EF Core or Anthropic.

## Links

[[../data-model]] · [[../../../CONTEXT]] invariant 5
