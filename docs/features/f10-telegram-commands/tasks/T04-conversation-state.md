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

## Implementation

Three parts, each built test-first, splitting the state value from the pure turn decision and from the
Redis store that holds it — so the whole AC-08 machine is decided without a store and the store is the
only place a real Redis path runs.

- **The state — `ConversationState` + `IConversationStateStore` (Domain).** The value is one pending
  command: its `Command`, the `Awaiting` slot, an optional `Context` map and the `StartedAt` instant.
  Its `Lifetime` is a domain constant of five minutes and `HasExpired(now, lifetime)` is inclusive at
  the boundary — at exactly 300 s the state is expired — so the 4:59/5:01 assertions (done-when #3) are
  a property of the type. The port is best-effort by contract: `GetAsync` returns null both when
  nothing is pending and when the store is down (indistinguishable, which is the correct degraded
  behaviour), and `SetAsync`/`ClearAsync` swallow outages. The store never decides expiry — a document
  the store still holds is by construction live.
- **The decision — pure `ConversationTurnResolver` (Application).** Given whatever is pending, the
  incoming message and the clock instant it returns a `ConversationTurn` carrying a
  `ConversationDisposition` — `Proceed`, `Resume`, `Superseded`, `Cancelled`, `NothingToCancel` or
  `Expired`. Two rules order the branches. Expiry wins over everything: a pending command past its
  lifetime is reported `Expired` and never swallows the next message, whether that message is free text
  or another command (the Redis TTL would already have removed it; this is the same rule for a caller
  that read a still-live copy). Below that, `/cancel` is always honoured — including a
  `/cancel@BotName` group suffix — a new command supersedes a live pending one and says so (done-when
  #4), and any other message resumes it with the message verbatim as input (done-when #1). With nothing
  pending, `/cancel` is a cheerful no-op (`NothingToCancel`, done-when #2) and anything else proceeds.
  It holds no store and no clock, so every branch is unit-tested in isolation.
- **The store — `RedisConversationStateStore(IConnectionMultiplexer, IClock)` (Infrastructure).** A
  pending conversation lives under `{env}:jobhunter:convstate:{chat_id}` as one small JSON document
  with a native TTL — the TTL *is* the expiry (done-when #6), so no sweeper can fail and wedge a chat
  and a pod restart cannot wedge one either. The TTL written is the *remaining* lifetime from now
  (`Lifetime − (now − StartedAt)`), so a state stored late in a step still expires on the same
  wall-clock deadline rather than resetting the window; a non-positive remainder is simply not stored.
  Every operation is wrapped: a `RedisException` on read degrades to null (multi-step commands fall
  back to requiring the argument inline; read commands never touch the store, so they are unaffected —
  done-when #5), and a failed set or clear is swallowed. The one real Redis path is exercised once, in
  a Docker-gated integration test (round trip, TTL ≤ lifetime, clear, independence); the outage-degrade
  contract is proved with an unreachable multiplexer, no Docker needed.

**Deferred to [[T10-menu-help-conformance|T10]].** The live wiring — reading the pending state before
dispatch, running the resolver at the head of `OwnerGatedUpdateProcessor`, and persisting/clearing
state around a multi-step command — lands with T10, alongside the confirmation-nonce step (T05) that
completes the SAD §6.1 chain. Wiring a live path through a partial registry before then would be
throwaway.

## Links

[[../sad]] §6.2 · [[../data-model]] §Conversation state
