# T14 — Grow the company registry with pure-play AI / dev-tools / infra employers

**Layer:** app · **Deps:** T03 · **Est:** L · **Owner:** Viacheslav

## What

`tools/seed/companies.yaml` currently holds ~30 curated entries against a designed universe of ~300
(ADR-F1-0001). Coverage is the hard ceiling on what the Owner ever sees, and several top target
employers are missing. Grow the seed toward ~300 with pure-play AI, developer-tools and infrastructure
employers, verifying the ATS binding per entry (the same schema T03 validates: `domain`, `display_name`,
`ats_kind`, `board_token`, optional `careers_url` / `hq_country`).

Candidates to add (verify each entry's live ATS binding before committing; skip any that cannot be
bound to a supported provider):

OpenAI, Cursor/Anysphere, LangChain, Perplexity, Cohere, Hugging Face, Together AI, Replicate, Pinecone,
Weaviate, Modal, Baseten, Temporal, Fly.io, Railway, Render, Grafana Labs, Confluent, dbt Labs, Retool,
Zapier, Replit, Warp, Zed, Notion, Supabase, Neon, Airbyte.

## Done when

- `companies.yaml` is materially expanded toward the ~300 target with the AI / dev-tools / infra
  employers above (those with a verifiable supported ATS binding).
- Every added row carries a real, verified `ats_kind` + `board_token`; entries without a bindable ATS
  are left out rather than guessed. `domain` stays the unique natural key (no duplicates).
- `seed` stays idempotent — re-running after the expansion reports zero further inserts.
- The file schema-validates; a malformed added entry fails the command naming the line.
- Provenance stays `Curated` for every hand-added row (the crawl proposes `DirectoryCrawl` entries
  separately, inactive).

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-09 ·
`tools/seed/companies.yaml` · [[T03-registry-seeding]] ·
[[../adr/0001-company-registry-seeding|ADR-F1-0001]]
