# T05 — Confirmation flow for state-changing commands

**Layer:** app/telegram · **Deps:** T04 · **Est:** M · **Owner:** Viacheslav

## What

`ConfirmationService`: issue a single-use nonce with a 2-minute TTL, render a keyboard
naming the **exact effect**, validate and burn on tap. Required for every `ChangesState` command —
the descriptor decides, not the handler author.

## Done when

- Every state-changing command requires a confirmation naming its effect, and for `/run` and `/cv` also the estimated cost (AC-07).
- A nonce is single-use — a second tap says it was already used.
- A nonce expires at 2 minutes; the reply asks for the command to be re-issued.
- The payload fits Telegram's 64-byte callback cap.
- A handler cannot be reached for a `ChangesState` command without a validated nonce — asserted by an architecture test.

## Links

[[../sad]] §6.3 · [[../adr/0001-declarative-command-registry|ADR-F10-0001]]
