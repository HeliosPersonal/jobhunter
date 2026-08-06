# T01 — Domain: Signal, PreferenceModel, PreferenceWeight

**Layer:** domain · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

`Signal`, `SignalKind`, `PreferenceModel`, `PreferenceWeight` and `Dimension`. The
construction guard carries the ADR: a `PreferenceWeight` cannot be constructed with fewer than three
supporting signal ids, so the evidence floor is a type-level property rather than a validation step
that can be skipped.

## Done when

- Constructing a `PreferenceWeight` with fewer than 3 supporting signal ids throws (AC-03).
- `Signal` requires a non-empty `job_facts` snapshot — a signal without facts teaches nothing.
- Signal weights per kind match [[../sad|SAD]] §8 and come from configuration.
- `PreferenceModel` is immutable; activation is a separate operation.
- The seven dimensions are a closed enum.

## As built

Five types in `src/JobHunter.Domain/Preferences/`, all pure and test-covered with no persistence:

- **`Dimension`** — closed enum, the seven SAD §8 dimensions (`SalaryBand`, `Country`, `CompanySize`,
  `Technology`, `TimezoneBand`, `RemotePolicy`, `EmploymentType`). `AiUsage`/`RoleFamily` are deferred to
  T10 (TUNE-08), documented on the enum rather than stubbed.
- **`SignalKind`** — four card actions (`Opened`, `Ignored`, `Saved`, `Rated`; F5 writes) and four
  outcomes (`Applied`, `Interview`, `Offer`, `Rejected`; F6 writes).
- **`SignalWeights`** — a `ValueObject` holding the SAD §8 table with `Default = (1, 2, 3, 4, 6)`. The four
  card actions collapse to one weight via `WeightFor(kind)`; each outcome carries its own. This is the
  "from configuration" hook — the weights are a value, not literals sprinkled through the code.
- **`JobFacts`** — a `ValueObject` snapshot keyed by `Dimension`, built through `Create(...)` which trims,
  drops blanks, dedups (Ordinal) and **rejects an all-empty map** (the "a signal without facts teaches
  nothing" guard). Equality uses a per-dimension boundary marker so `{A:[x],B:[y]}` ≠ `{A:[x,y]}`.
- **`Signal`** — `Entity`. Guards: job ref required, non-empty facts, weight > 0, and the card-action /
  outcome split (outcomes MUST carry an `applicationId`, card actions MUST NOT). `Signal.Capture(...)`
  resolves the weight from `SignalWeights` so F5/F6 never hand-copy the table. Idempotent capture is a DB
  concern (unique `(job_id, kind, occurred_at)`), not enforced here.
- **`PreferenceWeight`** — `Entity`. `MinSupportingSignals = 3` is the ADR-F7-0002/AC-03 floor as a
  construction guard: fewer than three **distinct, non-empty** ids is unrepresentable. `Weight` bounded to
  `[-1, 1]`, `PositiveRate` to `[0, 1]`. `Disable(at)` is idempotent (keeps the first timestamp); a
  disabled weight is retained, never deleted (AC-06).
- **`PreferenceModel`** — `Entity`. `ActivationThreshold = 200`. Immutable; `Activate(at)` is a **separate**
  operation guarded by `HasSufficientEvidence` (throws below the floor), idempotent. `Deactivate()` lets a
  refit flip versions atomically (SAD §4 S6, the caller's responsibility). An empty weight set is legal —
  the indifferent Owner earns a model with no weights.

Tests: `SignalWeightsTests`, `JobFactsTests`, `SignalTests`, `PreferenceWeightTests`,
`PreferenceModelTests` in `tests/JobHunter.Domain.Tests/Preferences/`. Persistence (the `signals`,
`preference_models`, `preference_weights` tables) is T02.

## Links

[[../adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]] · [[../data-model]]
