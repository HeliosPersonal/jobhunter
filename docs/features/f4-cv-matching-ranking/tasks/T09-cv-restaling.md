# T09 — CV activation, re-staling and re-match scheduling

**Layer:** app · **Deps:** T03, T06 · **Est:** M · **Owner:** Viacheslav

## What

`CvActivationHandler` and `ReMatchScheduler`: on activation, mark matches from older CV
versions not-current and queue live jobs from the last 30 days for cheap-tier re-match on the next
Run. Old matches are marked, never deleted
([[../adr/0002-cv-versioning-and-restaling|ADR-F4-0002]]).

## Done when

- Activating a version marks every older-version match `is_current = false` (AC-08).
- No match row is ever deleted by this path — asserted by a row-count comparison.
- Exactly the last 30 days of live jobs are queued; the boundary is asserted at 29, 30 and 31 days.
- Re-match items are submitted at cheap tier and ledgered like any other work.
- Re-uploading identical content triggers no re-staling and no re-match.

## Links

[[../adr/0002-cv-versioning-and-restaling|ADR-F4-0002]] · [[../PRD]] AC-08
