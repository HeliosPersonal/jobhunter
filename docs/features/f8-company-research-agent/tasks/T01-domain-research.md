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

## Implementation

The aggregate lives in `src/JobHunter.Domain/Research/`, with no dependency on HTTP, EF Core or
Anthropic (done-when 5): it is assembled from values the Application layer has already fetched, verified
and discarded.

- **`ResearchCategory`** — the eight categories as a closed enum, persisted as `text`. A count-locking
  test (`ResearchCategoryTests`) makes a ninth a deliberate schema change, not an accident.
- **`Freshness`** — a pure static: `ThresholdFor` returns 7 days for `News`/`Layoffs` and 30 for the
  rest, and `IsStale(generatedAt, category, now)` takes the current time as an argument (done-when 3, D5)
  so it depends on no ambient clock. The threshold boundary is inclusive of fresh, and a future
  generation time (clock skew) is never stale.
- **`ResearchSource`** — one fetched document (the citation authority). Guards a non-blank URL and a
  non-negative text length; the title may be blank because some feeds omit it. Its `ObservedAt` is the
  fetch time a claim inherits (AC-03).
- **`ResearchClaim`** — the invariant-5 type. The constructor takes a `ResearchSource` object, not a
  bare id, so an uncited claim is unrepresentable rather than merely rejected (done-when 1, AC-02); it
  copies `ObservedAt` from that source, never accepting one independently (done-when 4, AC-03).
- **`CompanyResearch`** — the dossier. It verifies every claim rests on one of its own recorded sources
  (a claim citing an unrecorded source is an uncited claim by the back door), derives
  `CategoriesCovered` from the claims rather than storing it, records `CategoriesUnavailable` explicitly
  (AC-07), forbids a category being both covered and unavailable, and orders warnings first (AC-04).

## Links

[[../data-model]] · [[../../../CONTEXT]] invariant 5
