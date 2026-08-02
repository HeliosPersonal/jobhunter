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

## Links

[[../../../CONTEXT]] invariant 11 · [[../../f5-daily-digest-telegram/tasks/T03-digest-assembler|F5 T03]]
