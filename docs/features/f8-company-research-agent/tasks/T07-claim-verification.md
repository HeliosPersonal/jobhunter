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
