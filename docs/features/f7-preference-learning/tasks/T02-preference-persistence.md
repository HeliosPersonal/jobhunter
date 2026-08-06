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

## As built

Migration `F7AddSignalsAndPreferences` creates the four F7 tables (`signals`, `preference_models`,
`preference_weights`, `suppression_overrides`) with all seven declared indexes. Six come from the EF
configurations in `src/JobHunter.Infrastructure/Persistence/Preferences/`; the seventh,
`uq_preference_models_active`, is a partial unique index over the constant expression `(is_active)` filtered
to `WHERE is_active` — EF cannot model that, so it is raw SQL in the migration (the same pattern as
`uq_runs_single_active`) and named in `PreferenceModelConfiguration` for documentation only.

Write side, two ports in `Domain/Abstractions`:

- **`ISignalRepository` → `SignalRepository`** — a raw `INSERT ... ON CONFLICT (job_id, kind, occurred_at)
  DO NOTHING RETURNING id` (the `DeliveryLog` idempotence pattern), so `TryCaptureAsync` returns `true` on a
  genuine insert and `false` on a redelivery, with the database as the arbiter of idempotence. `job_facts`
  goes in as `jsonb` through `JobFactsJson`. No update or delete path — a signal is a fact about the past.
- **`IPreferenceModelRepository` → `PreferenceModelRepository`** — EF, model + weights as one owned-aggregate
  insert. `FindActiveAsync` (partial-index-served), `LatestVersionAsync` (next version number). A refit's
  atomic deactivate-then-activate happens in one `SaveChangesAsync` so the active partial index never trips
  mid-transaction.

`SuppressionOverride` / `SuppressionMode` are added to the domain and their table ships now (F7 owns the
schema), but no suppression repository is written yet — its consumer is T07/T08.

**Deviations from the data-model, deliberate:**

- **`preference_weights.supporting_signal_count` is not a stored column.** The count is derived from the
  `supporting_signal_ids` jsonb (`PreferenceWeight.SupportingSignalCount`), the same way `DigestCard` derives
  its reason count — persisting a computed, getter-only property EF can't write would only risk drift from
  the ids that are the actual evidence. The data-model table is annotated accordingly.
- **`signals.application_id` carries no FK yet.** The `applications` table is F6; the column is a plain
  nullable `uuid` here and F6's migration adds the foreign key when that table exists.

Tests: `PreferencePersistenceTests` (Docker-gated) asserts all seven indexes exist, the idempotent capture
and its unique constraint, the round-trip of a model with its weights and evidence, the "exactly one active"
partial index, the atomic refit flip, the one-rule-per-value override constraint, and the two query-plan
coverage checks (`idx_signals_window`, `idx_preference_weights_lookup`). Domain guards for the two new types
are in `SuppressionOverrideTests`.

## Links

[[../data-model]]
