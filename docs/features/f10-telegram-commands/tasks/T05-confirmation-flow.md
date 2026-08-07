# T05 — Confirmation flow for state-changing commands

**Layer:** app/telegram · **Deps:** T04 · **Est:** M · **Owner:** Viacheslav

## What

`ConfirmationService`: issue a single-use nonce with a 2-minute TTL, render a keyboard
naming the **exact effect**, validate and burn on tap. Required for every `ChangesState` command —
the descriptor decides, not the handler author.

## Done when

- Every state-changing command requires a confirmation naming its effect, and for `/run` and `/research` also the estimated cost (AC-07).
- A nonce is single-use — a second tap says it was already used.
- A nonce expires at 2 minutes; the reply asks for the command to be re-issued.
- The payload fits Telegram's 64-byte callback cap.
- A handler cannot be reached for a `ChangesState` command without a validated nonce — asserted by an architecture test.

## Implementation

Three pieces, one per layer, each built test-first.

**Domain — `ConfirmationToken` + `IConfirmationStore`.** `ConfirmationToken` is the single-use
credential: a `Nonce`, the `ChatId` it was issued to, the `Command` and `ArgumentTail` it authorises,
its `IssuedAt`, and a `Used` flag. `Lifetime` is a fixed 2 minutes; `HasExpired(now, lifetime)` uses an
inclusive boundary (`now - IssuedAt >= lifetime`), so at exactly 2 minutes the token is expired.
`IConfirmationStore` declares the contract that makes the flow safe: `IssueAsync` **fails closed** — it
does not swallow a store outage, so a state-changing command is never shown a confirmation that was
never persisted — and `RedeemAsync` atomically reads-and-burns, so two taps of the same nonce cannot
both confirm.

**Application — `ConfirmationService`.** The service owns the decision, not the store. `IssueAsync`
stamps a fresh URL-/callback-safe nonce (16-byte id → 22 base64url chars, well under the 64-byte
callback cap) via `IIdGenerator`, binds it to the chat/command/argument tail at `IClock.UtcNow`, and
persists it. `RedeemAsync` interprets the store's pre-burn snapshot into a `ConfirmationResult`:
absent → `Expired`; `Used` → `AlreadyUsed`; wrong chat → `Mismatch`; past `Lifetime` even if the TTL
has not yet swept it → `Expired` (belt-and-braces on the clock); otherwise `Confirmed`, and only a
`Confirmed` result carries the command to run — no refusal is shaped like a runnable command.

**Infrastructure — `RedisConfirmationStore`.** One JSON document under
`{env}:jobhunter:confirm:{nonce}` with a **native TTL that is the expiry** — no sweeper to fail. The
TTL is the *remaining* lifetime from `IssuedAt`, so a token stamped late still expires on its original
deadline, and a non-positive remainder writes nothing. Single use is atomic: `RedeemAsync` runs one Lua
script that `GET`s the document and, in the same round trip, claims a companion `:used` marker with
`SET NX PX <ttl>` — the first tap takes the marker and reads the token unused, every later tap finds it
already held and reads used. The document is **not** deleted on redemption, so a second tap sees a
*used* token ("already used") rather than an absent one ("expired"); the TTL removes both together.
Consistent with the fail-closed contract, `IssueAsync` does not wrap the write (an outage surfaces),
while `RedeemAsync` catches `RedisException` and returns `null` so an unreachable store refuses rather
than runs unconfirmed.

**Deferred to T10.** As with T03/T04, this task ships the mechanism, not the live wiring.
`RedisConfirmationStore` is not yet registered in Infrastructure DI, and the dispatch path is not yet
routed through `ConfirmationService` — both land with T10 alongside the dispatch rewire against the full
command registry. The done-when clause *"a handler cannot be reached for a `ChangesState` command
without a validated nonce — asserted by an architecture test"* is likewise satisfied in T10, once the
dispatcher and handlers exist to assert against.

## Links

[[../sad]] §6.3 · [[../adr/0001-declarative-command-registry|ADR-F10-0001]]
