# T09 — Reprocessing and retention

**Layer:** app · **Deps:** T06 · **Est:** M · **Owner:** Viacheslav

## What

A `reprocess` command re-running normalisation and deduplication over stored raw
payloads with zero network, preserving job identities where the fingerprint is unchanged so
downstream references stay valid (AC-09). Plus the retention job pruning raw payloads older than
90 days.

## Done when

- Reprocessing makes zero HTTP calls — asserted with a handler that throws on any request.
- Jobs whose fingerprint is unchanged keep their id; enrichments and matches stay attached.
- A changed fingerprint creates a new job and records the old one as superseded rather than orphaning it silently.
- Throughput ≥ 5 000 postings/min.
- Retention pruning never deletes a raw posting still referenced by a live alias.
- The command is operator-scoped.

## Links

[[../PRD]] AC-09 · [[../../../ARCHITECTURE-OPEN-DECISIONS|O3]]
