# T03 — Owner action handler

**Layer:** app · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

Consume `OwnerActionRecorded` from F5: create the application lazily if absent, evaluate
the transition, apply it, record the history row, and publish `ApplicationStatusChanged` — all in one
transaction.

## Done when

- A digest action creates or advances the application and appears in its history (AC-04).
- An application is created only on first action — a delivered card with no action creates nothing (SAD §4 S2).
- A refused transition leaves the status and the history unchanged (AC-02).
- Two rapid identical actions produce one transition — the handler is idempotent.
- The status change, the history row and the outbox message commit together.

## Links

[[../sad]] §6.1 · [[../../f5-daily-digest-telegram/tasks/T10-callback-actions|F5 T10]]
