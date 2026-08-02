# T02 — Migration and repositories for digests and delivery log

**Layer:** infra/db · **Deps:** T01 · **Est:** S · **Owner:** Viacheslav

## What

Migration `F5_AddDigestsAndDeliveryLog` with the six indexes, the most important being
unique `(run_id, chat_id, card_key)` — which *is* [[../../../CONTEXT]] invariant 8. The repository
exposes no update or delete path for the log.

## Done when

- Migration applies on a clean database; all six indexes exist with declared names.
- A duplicate delivery-log row is rejected — asserted by violating the constraint.
- The repository has no update and no delete method for `delivery_log`.
- The already-delivered query is covered by `idx_delivery_log_run_chat`, verified with a query plan assertion.
- One digest per Run is enforced.

## Links

[[../data-model]] · [[../../../CONTEXT]] invariant 8
