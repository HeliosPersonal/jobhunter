---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f1-ats-job-discovery, jobhunter]
---

# F1-0002 — Immutable raw postings with content-hash deduplication

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

A board is fetched four times a day; the overwhelming majority of postings are byte-identical
between fetches. We must decide what to store, when, and whether stored payloads may ever be
modified. This decision determines storage growth, downstream event volume, and whether history can
be reprocessed when normalisation improves.

## Decision drivers

- Downstream volume should reflect genuine change, not fetch cadence — otherwise F2 and F3 do four
  times the necessary work.
- Normalisation *will* improve; reprocessing must not require re-fetching every provider.
- The dedup decision must be cheap: it happens ~1 200 times a day and must not require parsing.
- [[../../../CONTEXT]] invariant 1 already states raw postings are immutable — this ADR records why.

## Considered options

1. **Upsert a single row per `(source, external_id)`**, overwriting the payload each fetch.
2. **Store every fetch**, unconditionally.
3. **Store a new row only when the content hash changes**; unchanged fetches bump `last_seen_at`.
4. **Store no raw payload**; normalise on the fly and keep only `jobs`.

## Decision outcome

**Chosen: Option 3.**

The hash is computed over the payload with volatile fields stripped — provider-side timestamps,
tracking parameters, view counters — so a cosmetic change does not masquerade as a real one. Insert
is a single statement:

```sql
INSERT INTO raw_postings (…) VALUES (…)
ON CONFLICT (source_id, external_id, content_hash)
DO UPDATE SET last_seen_at = excluded.last_seen_at;
```

One round trip, no read-then-write race, and the `xmax = 0` trick distinguishes an insert from a
conflict so the event is published only on genuine change.

Rows are never updated except for `last_seen_at`, and never deleted except by the retention job.
The repository exposes no method that writes `payload` after insert; an architecture test asserts it.

Option 1 loses history and makes reprocessing impossible. Option 2 quadruples storage for no
information. Option 4 makes every normalisation improvement a re-crawl of the entire internet.

## Consequences

**Positive**
- Downstream event volume tracks real change: ~150/day instead of ~1 200.
- History is reprocessable — the whole corpus can be re-normalised offline (QG-3).
- A posting's edit history is queryable: successive rows for one `external_id` are its versions.

**Negative**
- Storage grows with change rather than staying flat. Bounded by 90-day retention
  ([[../../../ARCHITECTURE-OPEN-DECISIONS|O3]]) and compression if it exceeds 5 GB.
- Volatile-field stripping is provider-specific and must be maintained per adapter; a missed field
  causes spurious "changes". Detected by the unchanged-ratio metric, which should sit near 90%.

**Neutral**
- `last_seen_at` doubles as the liveness signal: a posting not seen for two cycles is a candidate
  for closure, which F2 uses.

## Links

- [[../../../CONTEXT]] invariant 1 · [[../data-model]] · [[../sad]] §10 QG-3
