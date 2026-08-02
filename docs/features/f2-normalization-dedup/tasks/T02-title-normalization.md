# T02 — Title normalisation and seniority extraction

**Layer:** app · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

`TitleNormalizer`: case fold, collapse whitespace, strip bracketed and post-separator
decoration, canonicalise seniority abbreviations, and extract seniority into its own field. The
published title is never modified — normalisation produces a *second* value used only for comparison
(AC-05).

## Done when

- ≥ 95% accuracy on the 200-title labelled set; the test fails below 190.
- `Sr.`, `Senior`, `Snr` and `III` all canonicalise to the same seniority.
- `Backend Engineer (Remote)` and `Backend Engineer - EMEA` normalise identically; `Backend Engineer | Payments` records `Payments` so the team distinction is not lost.
- `jobs.title` is byte-identical to the provider's value across every fixture.
- Deterministic under `tr-TR`, `de-DE` and `en-US`.

## Out of scope

- Model-based title understanding — F3.

## Links

[[../sad]] §8 · [[../test-plan]] §NFR
