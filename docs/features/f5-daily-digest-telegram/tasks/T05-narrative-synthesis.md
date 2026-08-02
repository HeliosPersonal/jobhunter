# T05 — Narrative synthesis with template fallback

**Layer:** claude · **Deps:** T03 · **Est:** M · **Owner:** Viacheslav

## What

One deep-tier synthesis call through F3's machinery producing the market note, plus the
template fallback used when the call is unavailable or over budget. The narrative is optional by
design — a provider outage must not cost the digest.

## Done when

- A successful call produces a narrative and sets `narrative_source = Model`.
- An unavailable provider or an exhausted budget produces a template narrative and sets `narrative_source = Template` — the digest still ships.
- Narrative text is escaped like any other dynamic value.
- The synthesis submission is ledgered and ceiling-checked like every other batch.
- The prompt is snapshot-tested so a change is visible in a diff.

## Links

[[../../f3-claude-batch-enrichment/tasks/T10-enrichment-submit|F3 T10]] · [[../adr/0001-never-delay-the-digest|ADR-F5-0001]]
