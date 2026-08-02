# T03 — Company registry seeding and expansion

**Layer:** app · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

`tools/seed/companies.yaml` with ~300 curated companies, plus a `seed` CLI command that
upserts them idempotently. Plus the weekly expansion job that proposes companies from public ATS
directories with `source = 'DirectoryCrawl'` and `is_active = false`, per
[[../adr/0001-company-registry-seeding|ADR-F1-0001]].

## Done when

- `seed` is idempotent — running it twice changes nothing and reports zero inserts.
- The YAML is schema-validated; a malformed entry fails the command naming the line.
- Crawled companies are inserted inactive and never activated automatically.
- `source` is recorded on every company so a bad batch is revertible by provenance.
- Seeded data is realistic enough that local discovery produces jobs on first run.

## Out of scope

- Deciding *which* companies — that is curation, not code.

## Links

[[../adr/0001-company-registry-seeding|ADR-F1-0001]] · [[../../../ARCHITECTURE-OPEN-DECISIONS|O1]]
