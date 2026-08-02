# T08 — Delivery handler with per-card idempotence

**Layer:** app · **Deps:** T02, T06, T07 · **Est:** L · **Owner:** Viacheslav

## What

`DeliveryHandler` consuming `DigestReady`: load the digest, load already-delivered card
keys, send only the remainder, and write a delivery-log row **immediately after each successful send**
([[../adr/0002-delivery-idempotence|ADR-F5-0002]]).

## Done when

- A clean delivery of 10 cards produces 12 messages and 12 log rows.
- Killing delivery after card 3 and restarting sends exactly the remaining 7 — no card twice (AC-04, QG-2).
- Retrying a completed delivery sends nothing.
- Two racing handlers cannot double-send; the unique constraint arbitrates.
- A 400 on one card logs that card as failed and delivers the rest.
- Every case in [[../test-plan|test-plan]] §The duplicate-delivery suite passes.

## Links

[[../adr/0002-delivery-idempotence|ADR-F5-0002]] · [[../sad]] §10 QG-2
