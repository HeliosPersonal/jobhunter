# T04 — Apply-link verification

**Layer:** app · **Deps:** T03 · **Est:** S · **Owner:** Viacheslav

## What

Verify each candidate card's apply destination before presenting it, with a 5 s timeout
and bounded parallelism. The verifier produces one of three outcomes: **reachable**,
**confirmed-unreachable** (a definitive 4xx/5xx or a DNS failure), or **unverified** (a timeout, or a
URL the politeness handler cannot fetch because `robots.txt` disallows it). Only a
**confirmed-unreachable** link drops a card; an **unverified** result keeps the card and flags it
"link unverified", because a slow host or a robots-blocked path is not a closed job. Timeout ≠
confirmed-unreachable.

## Done when

- A confirmed-unreachable destination — a definitive 4xx/5xx response or a DNS failure — excludes the card (AC-11); `apply_url_verified = false` and it is not presented as an actionable card.
- A **timeout** keeps the card, records verification as inconclusive, and renders it with the "link unverified" flag rather than dropping it.
- A **robots-disallowed** apply URL is unverifiable, not unreachable: the card is kept and shown with the same "link unverified" flag.
- Verification runs with bounded parallelism and never exceeds the assembly window.
- Requests go through the shared politeness handler, so verification is rate-limited like any other fetch and honours `robots.txt`.
- A job whose link is confirmed-unreachable is also flagged for the lifecycle sweep, so F2 can close it.

## Links

[[../PRD]] AC-11 · [[../sad]] §11 D3 · [[../../f1-ats-job-discovery/tasks/T04-politeness-handler|F1 T04]]
