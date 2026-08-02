# T11 — Raw posting ingestion with content-hash dedup

**Layer:** app · **Deps:** T10 · **Est:** M · **Owner:** Viacheslav

## What

The insert path: a single `ON CONFLICT DO UPDATE SET last_seen_at` statement that both
deduplicates and refreshes liveness, using the `xmax = 0` trick to distinguish a genuine insert from
a conflict so `RawPostingIngested` is published only on real change
([[../adr/0002-immutable-raw-postings|ADR-F1-0002]]).

## Done when

- Byte-identical content creates no new row and publishes no event (AC-02).
- Changed content creates a new row and publishes exactly one event.
- The insert is one round trip — no read-then-write, no race between concurrent fetches.
- The unchanged-content ratio is exported as a metric (expected ≈ 90%).
- The handler is idempotent: replaying the same message produces no second row and no second event.

## Links

[[../adr/0002-immutable-raw-postings|ADR-F1-0002]] · [[../../../architecture/event-catalog]]
