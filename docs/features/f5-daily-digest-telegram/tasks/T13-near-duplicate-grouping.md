# T13 — Near-duplicate grouping at digest assembly

**Layer:** app · **Deps:** T03 · **Est:** S · **Owner:** Viacheslav

## What

`NearDuplicateGrouper`, applied during digest assembly. Two jobs that are the *same real opening*
posted under slightly different titles or on two boards are grouped into one card for display, with
the others reachable, so the digest never shows the Owner the same role twice. Relocated here from F2
because grouping is a **presentation** concern computed at digest assembly, not a canonicalisation
concern ([[../../f2-normalization-dedup/adr/0001-conservative-fingerprint|ADR-F2-0001]] — "computed
at digest assembly"). F2 owns canonical `Job` identity; F5 owns how near-duplicates are *shown*.

## Done when

- Near-duplicate jobs are grouped into one presented card during assembly; the grouped-away jobs
  remain queryable and are not lost (the F2 dedup grouping property, now realised at display time).
- Grouping runs on the assembled card set, after selection and before persistence, so the grouping is
  snapshotted onto the digest like everything else and a replay reproduces it.
- Grouping is deterministic — the same card set always groups the same way and picks the same
  representative card.
- The grouper reads only the assembled cards and their jobs; it does not re-open the dedup pipeline.
- The footer/counters reflect grouped cards, so the "N shown" count matches what is actually rendered.

## Links

[[../sad]] §6.1 · [[../../f2-normalization-dedup/adr/0001-conservative-fingerprint|ADR-F2-0001]] ·
[[T03-digest-assembler|T03]]
