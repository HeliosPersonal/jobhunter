# T07 — Claim verification

**Layer:** app · **Deps:** T06, T04 · **Est:** M · **Owner:** Viacheslav

## What

`ClaimVerifier`: check each returned claim's cited URL against the dossier's fetched set,
store the matched, **discard** the unmatched. Exact match after normalising scheme, host case and
trailing slash — deliberately not fuzzy, because a claim citing a URL "close to" a real one is the
failure mode being guarded against.

## Done when

- Every case in [[../test-plan|test-plan]] §The uncited-claim suite passes.
- A trailing-slash or host-case difference still matches; a different path does not.
- A URL from another company's dossier is discarded — the fetched set is scoped per dossier.
- Discarded claims are counted on the dossier and logged with the fabricated URL (AC-08).
- A dossier where every claim was fabricated is stored with zero claims and the count recorded.
- Verification is a set-membership check, not a similarity score — asserted by a test that a near-miss is rejected.

## Links

[[../adr/0001-fetch-then-synthesise|ADR-F8-0001]] · [[../sad]] §10 QG-1

## Implementation

Three types in `src/JobHunter.Application/Research/`. The verifier is a **pure Application-layer**
function of its inputs and its log — no clock, no id generator: a matched claim inherits its observed
date from the source it cites, and the orchestrator (T08) mints the `ResearchClaim` id, so nothing here
touches identity or time.

- **`UnverifiedClaim`** — the parsed-but-not-yet-trusted claim as the synthesiser returned it, carrying a
  bare `SourceUrl` string rather than a `ResearchSource`. The claude layer maps its wire `ClaimDto` onto
  this, so `JobHunter.Application` depends on its own type and never references `JobHunter.Claude` (the
  same boundary crossing enrichment parsing uses). A `ResearchClaim` cannot even be constructed until the
  URL is proven, because its constructor takes a source object, not an id — invariant 5 by construction.
- **`ClaimVerifier.Verify(sources, claims)`** — builds the set of normalised fetched URLs for **this
  dossier** and partitions the claims: a claim whose normalised cited URL is in the set is returned as a
  `VerifiedClaim` paired to the exact `ResearchSource` that substantiates it; every other claim is counted
  as discarded and its fabricated URL logged at `Warning` (AC-08). The count is returned on
  `ClaimVerification.Discarded`; discarded claims leave no other trace. A dossier whose every claim is
  fabricated returns zero verified claims and the full count — T08 still stores the dossier, with no claims.
- **Normalisation** is the one permitted tolerance and nothing more: lowercase scheme and host, drop a
  single trailing slash, require an absolute HTTP(S) URI. A query parameter, a fragment or a different path
  changes the key and so fails set membership — the check is exact, not a similarity score
  (research-schema §Citation verification). An unparseable URL normalises to null and can never match, so a
  malformed citation is discarded, never thrown.

The uncited-claim suite (QG-1) drives all of this test-first: all-real → all stored, discard 0; one
never-fetched URL → that one discarded; trailing-slash and host/scheme case → still matched; different
path, injected query parameter and a URL from another company's dossier → discarded; every-fabricated →
zero claims stored with the count recorded; plus the near-miss rejection that proves membership, not
similarity. The structural "every `research_claims` row resolves to a same-dossier `research_sources` row"
assertion lands with T08's persistence, where the aggregate is built from these `VerifiedClaim`s.
