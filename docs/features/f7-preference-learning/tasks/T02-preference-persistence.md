# T02 — Migration and repositories

**Layer:** infra/db · **Deps:** T01 · **Est:** S · **Owner:** Viacheslav

## What

Migration `F7_AddSignalsAndPreferences` with the seven indexes. The `signals` table is
created here even though F5 and F6 write to it — F7 owns the schema, they own the rows.

## Done when

- Migration applies on a clean database; all seven indexes exist with declared names.
- Exactly one active model is enforced by the partial unique index; asserted by violating it.
- A duplicate signal for the same job, kind and moment is rejected — capture is idempotent.
- The fitting-window query is covered by `idx_signals_window`, verified with a query plan assertion.
- The per-job weight lookup is covered by `idx_preference_weights_lookup`.

## Links

[[../data-model]]
