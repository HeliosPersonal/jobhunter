# T09 — Binding re-detection and ATS migration

**Layer:** app · **Deps:** T08 · **Est:** M · **Owner:** Viacheslav

## What

A weekly job that re-detects bindings older than seven days and for companies whose
board has returned zero postings for two consecutive cycles. When a company has migrated, retire the
old binding and record the new one — without orphaning any previously discovered job, because the
key is the company, not the board token.

## Done when

- A company that migrates provider gets the old binding retired and a new one recorded (AC-05).
- Jobs discovered under the old binding remain attached to the same company.
- A board that legitimately has zero openings does not trigger retirement — two empty cycles plus a successful probe of another provider is required.
- Retired bindings are never deleted, so the migration is auditable.
- Re-detection is spread across the week rather than stampeding on Monday.

## Links

[[../data-model]] §ats_bindings · [[../PRD]] AC-05
