# T01 — Domain: Digest, DigestCard, CardKey

**Layer:** domain · **Deps:** — · **Est:** S · **Owner:** Viacheslav

## What

`Digest`, `DigestCard`, `CardKey` and `DeliveryRecord`. `CardKey` is a deterministic
function of `(run_id, job_id)` — that determinism is what makes resumed delivery able to ask "which of
these have I already sent" without coordination.

## Done when

- `CardKey` is deterministic and stable: the same inputs produce the same key across processes and releases.
- A `DigestCard` cannot be constructed with an empty reasons list (AC-02).
- Reserved keys for the header and footer exist, so they use the same idempotence path.
- `Digest` records `narrative_source` so a template fallback is distinguishable after the fact.
- The aggregates have no dependency on Telegram or EF Core.

## Delivered

`JobHunter.Domain/Reporting/`:

- **`CardKey`** — a value object; `For(runId, jobId)` computes `sha256("N"(run) ‖ "N"(job))[..16]`,
  pinned by a stability test (`67c48f7c70c7c764`) so the hash cannot silently change and re-send every
  card. Reserved `__header__`/`__footer__` singletons route the header and footer through the same
  idempotence path. `TryCreate` rehydrates stored text without throwing.
- **`DigestCard`** — an `Entity`; rejects an empty/blank reasons list (invariant 4, AC-02), a rank below
  one and an out-of-range score. Computes its `CardKey` from `(run_id, job_id)` at construction.
- **`DeliveryRecord`** — an `Entity` modelling one append-only `delivery_log` row; header/footer carry a
  reserved key and a null `telegram_message_id`.
- **`Digest`** — the aggregate root. Two type-level guards: the suppressed count must reconcile to its
  breakdown (D7 / invariant 11 — a "34 hidden" footer whose reasons sum to 30 cannot be built), and a
  `Model` narrative must carry both text and a prompt version while a `Template` fallback must carry
  neither (SAD S4). Cards must have distinct ranks and belong to this digest; they are exposed
  rank-ordered.
- **`SuppressionTally`** (reason + non-negative count) and **`NarrativeSource`** (`Model`/`Template`,
  persisted as text) support the above.

No dependency on Telegram or EF Core. 62 tests in `Domain.Tests/Reporting/`; solution builds with zero
warnings.

## Links

[[../data-model]] · [[../adr/0002-delivery-idempotence|ADR-F5-0002]]
