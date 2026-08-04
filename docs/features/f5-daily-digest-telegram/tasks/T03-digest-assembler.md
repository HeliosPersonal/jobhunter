# T03 — Digest assembler and suppression summary

**Layer:** app · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

`DigestAssembler` consuming `RankingCompleted`: select cards (score ≥ 70, capped at 10),
snapshot their scores and reasons, build the suppression breakdown, gather carried-over and
degraded-source counts, and persist the digest **before** anything is sent.

## Done when

- Card selection honours the threshold and cap; boundaries asserted at 69, 70 and 11 qualifying.
- Scores and reasons are snapshotted onto the card, so re-running ranking cannot change a delivered digest.
- The suppression breakdown is grouped by reason with counts (AC-07).
- `avg_salary_usd` is null when fewer than three jobs carry a salary — better absent than misleading.
- A card with no reasons is excluded (AC-02).
- The digest is fully persisted before any send is attempted.

## Delivered

- **`DigestAssembler`** (Application/Reporting) — the handler for `RankingCompleted`. Loads the Run, the
  Run's scored candidates and the degraded sources, selects and snapshots the cards, builds the
  reconciling suppression breakdown, persists the whole digest **before** publishing `DigestReady` (S2),
  and is idempotent on `uq_digests_run` — a replayed completion finds the existing digest, re-emits
  `DigestReady` for it and writes nothing new. An unknown Run is warned and ignored.
- **Card selection** — shown candidates at or above `Digest:CardScoreThreshold` (default 70), best first,
  capped at `Digest:MaxCards` (default 10). A candidate whose reasons are all blank is *excluded* rather
  than thrown on, so an unexplained job never reaches the Owner (invariant 4, AC-02). The card copies the
  score and reasons, so a later re-score cannot change a delivered digest (QG-3). `StrongMatches` counts
  every shown score at or above the threshold, not just the carded ten.
- **`SuppressionSummarizer`** (Application/Reporting) — pure grouping of the suppressed candidates by
  reason into `SuppressionTally`s, ordered by descending count then reason (ordinal). A reason-less
  suppressed candidate folds under one explicit `Unspecified` bucket rather than being dropped, so the
  breakdown always reconciles to the suppressed count (invariant 11, AC-07).
- **Average salary** — the mean of the shown candidates' USD figures, rounded to two places, or null when
  fewer than `Digest:MinSalariesForAverage` (default 3) carry one. Only USD-denominated bands are
  averaged; a non-USD figure is left null rather than converted, because a fabricated FX rate is worse
  than an absent number.
- **`IDigestScopeQuery` / `DigestScopeQuery`** (Domain port + Dapper read model) — returns one
  `DigestCandidate` per `scores` row in the Run, *shown and suppressed*, joined by `LEFT JOIN LATERAL` to
  the job's current match for the reasons and the USD salary midpoint, ordered by `final_score DESC,
  job_id`. It returns every score (dropping the suppressed ones would make the footer a lie) and selects
  **nothing about the Owner** — the CV crosses exactly one boundary, and it is not this one.
- **`DigestReady`** (Contracts) — the integration event, idempotency-keyed on `RunId`. **`DigestOptions`**
  — the card threshold, cap and salary minimum, startup-validated via `.Validate().ValidateOnStart()`.

16 assembler + 5 summarizer unit tests (`Application.Tests/Reporting`, zero database) and 7
`[RequiresDockerFact]` integration tests for the read model
(`Infrastructure.Tests/Integration/DigestScopeQueryTests`); solution builds with zero warnings.

## Links

[[../sad]] §6.1 · [[../../../DECISION-LOG|D7]]
