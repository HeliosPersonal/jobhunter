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

## Links

[[../sad]] §6.1 · [[../../../DECISION-LOG|D7]]
