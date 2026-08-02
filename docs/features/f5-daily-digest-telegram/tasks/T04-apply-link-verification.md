# T04 — Apply-link verification

**Layer:** app · **Deps:** T03 · **Est:** S · **Owner:** Viacheslav

## What

Verify each candidate card's apply destination before presenting it, with a 5 s timeout
and bounded parallelism. Only a definitive gone-response drops a card; a timeout keeps it and flags
it, because a slow host is not a closed job.

## Done when

- A definitive 404 or 410 excludes the card (AC-11).
- A timeout or a 5xx keeps the card and records that verification was inconclusive.
- Verification runs with bounded parallelism and never exceeds the assembly window.
- Requests go through the shared politeness handler, so verification is rate-limited like any other fetch.
- A job whose link is dead is also flagged for the lifecycle sweep, so F2 can close it.

## Links

[[../PRD]] AC-11 · [[../../f1-ats-job-discovery/tasks/T04-politeness-handler|F1 T04]]
