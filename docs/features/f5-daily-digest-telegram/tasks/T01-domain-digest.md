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

## Links

[[../data-model]] · [[../adr/0002-delivery-idempotence|ADR-F5-0002]]
