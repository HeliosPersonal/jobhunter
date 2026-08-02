# T04 — Conversation state and cancellation

**Layer:** app · **Deps:** T03 · **Est:** M · **Owner:** Viacheslav

## What

Redis-backed per-chat state with a native 300-second TTL for multi-step commands, plus
`/cancel`. Redis rather than a table specifically because the TTL *is* the expiry mechanism — a
sweeper that fails would leave a chat permanently swallowing messages.

## Done when

- A pending command resumes on the next non-command message.
- `/cancel` clears any pending state and is a cheerful no-op when nothing is pending (AC-08).
- State expires at 5 minutes and the Owner is told it expired — asserted at 4:59 and 5:01 with `FakeClock`.
- A new command cancels a pending one and says so.
- A Redis outage degrades multi-step commands to requiring the argument inline; read commands are unaffected.
- No sweeper job exists — expiry is the TTL.

## Links

[[../sad]] §6.2 · [[../data-model]] §Conversation state
