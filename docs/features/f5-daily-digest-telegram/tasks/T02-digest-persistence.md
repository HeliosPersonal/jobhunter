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

## Delivered

- **Migration `F5AddDigestsAndDeliveryLog`** — creates `digests`, `digest_cards` and `delivery_log`
  with all six declared indexes (`uq_digests_run`, `uq_digest_cards_job`, `uq_digest_cards_key`,
  `idx_digest_cards_rank`, `uq_delivery_log`, `idx_delivery_log_run_chat`), asserted present by name on
  a clean database. `narrative_source` persists as text; the suppression breakdown and degraded-source
  labels as `jsonb`.
- **`IDigestRepository` / `DigestRepository`** — EF write path for the aggregate and its owned cards;
  `FindByRunAsync` includes the rank-ordered cards. One digest per Run is a database constraint
  (`uq_digests_run`); a second assembly for the same Run fails at commit.
- **`IDeliveryLog` / `DeliveryLog`** — the append-only log. `TryRecordAsync` is a raw
  `INSERT ... ON CONFLICT (run_id, chat_id, card_key) DO NOTHING RETURNING id`, so a first send and a
  replay are told apart in one round trip with no read-then-write race (invariant 8). There is no update
  and no delete path — asserted by reflection over both the port and its implementation. A duplicate row
  is rejected by `uq_delivery_log`, asserted by a raw insert that bypasses the upsert.
- The already-delivered read is index-served on `(run_id, chat_id)` and never a seq scan — verified by a
  query-plan assertion with `enable_seqscan = off`.

7 `[RequiresDockerFact]` integration tests in `Infrastructure.Tests/Integration/DigestPersistenceTests`;
solution builds with zero warnings.

## Links

[[../data-model]] · [[../../../CONTEXT]] invariant 8
