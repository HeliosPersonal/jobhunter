# T07 — Suppression evaluation and the card floor

**Layer:** app · **Deps:** T06 · **Est:** M · **Owner:** Viacheslav

## What

Suppression rules with recorded reasons, plus the floor that keeps learning from ever
emptying the digest: if suppression would leave fewer than three cards, the least-suppressed are
restored and the digest says so.

## Done when

- Every suppression records a human-readable reason quoting its evidence (AC-04, invariant 11).
- A suppressed job remains retrievable — nothing is deleted.
- If suppression would leave fewer than 3 cards, the least-suppressed are restored and the restoration is stated (QG-3).
- `NeverSuppress` overrides are honoured and the tension is recorded.
- With learning disabled entirely, only explicit preferences apply and the digest says so (AC-07).
- The suppression breakdown reaching the digest equals the count of suppressed score rows, per reason.

## Implementation

- **C1 — the card floor (QG-3).** `DigestAssembler` (`src/JobHunter.Application/Reporting/`) restores the
  least-suppressed jobs — the highest-scoring suppressed candidates, closest to the bar — when selection falls
  short of `DigestOptions.MinCards` (default 3). Restoration is display-only: a restored job's `scores` row
  stays `suppressed`, so the footer's count still reconciles to the database (invariant 11); only a suppressed
  candidate that still carries a usable reason can be restored, because the floor cannot manufacture an
  explanation (invariant 4). The digest freezes `RestoredCount` so the intervention is never silent, and it is
  counted after apply-link verification, so it reflects cards that actually shipped. O5 is settled
  ([[../../../ARCHITECTURE-OPEN-DECISIONS|O5]], decided 2026-08-07): the salary floor is a ranking down-weight,
  not a hard pre-filter — the hard filter is an explicit Owner opt-in, off by default — so a below-floor job is
  suppressible-and-restorable here rather than dropped before scoring.
- **C2 — Owner overrides.** A `NeverSuppress`/`AlwaysSuppress` override read port
  (`ISuppressionOverrideQuery`, Dapper impl in `src/JobHunter.Infrastructure/Persistence/Queries/`) is honoured
  at suppression evaluation: a `NeverSuppress` job is kept and the tension between the Owner's rule and the
  learned model is recorded as the shown reason, so the override is visible, not silent.
- **C3a — learning off (AC-07).** `LearningOptions.Enabled` (`src/JobHunter.Application/Preferences/`) is the
  whole-learner master switch (distinct from the per-weight `PreferenceWeight.Disabled` of AC-06). When off,
  `PreferenceModelQuery` returns null early without loading the model, so only explicit preferences shape the
  ordering. `Digest.LearningEnabled` is frozen at assembly (persisted via the `learning_enabled` column,
  migration `F7AddDigestLearningEnabled`, default true) rather than re-read at send time, so a re-rendered
  `/digest` replays the state the run actually had (S2). The footer states "Preference learning is off — ranked
  on explicit preferences only", and renders even on an otherwise-clean day so the Owner is always told.
- **C3b — reconciliation.** `DigestSuppressionReconciliationTests`
  (`tests/JobHunter.Infrastructure.Tests/Integration/`) drives the real `DigestScopeQuery` +
  `SuppressionSummarizer` against Postgres and compares the breakdown to a ground-truth SQL aggregate of the
  `scores` table, per reason — a query that dropped a suppressed row or a summarizer that miscounted a reason
  fails here. A raw-inserted suppressed row with a NULL reason (bypassing the `Score` aggregate's invariant-11
  guard, the only way one could exist) is still counted under the "Unspecified" bucket, never dropped.

## Links

[[../../../CONTEXT]] invariant 11 · [[../../f5-daily-digest-telegram/tasks/T03-digest-assembler|F5 T03]]
