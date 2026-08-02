# T06 — Fingerprint calculation and the deduplication handler

**Layer:** app · **Deps:** T04, T05 · **Est:** L · **Owner:** Viacheslav

## What

`FingerprintCalculator` per [[../adr/0001-conservative-fingerprint|ADR-F2-0001]], and
`DeduplicationHandler` consuming `JobNormalized`: compute the fingerprint, attempt the insert, then
publish either `JobDiscovered` (new) or record an alias and publish `JobDuplicateDetected`.

## Done when

- **Zero false merges on the labelled corpus** — a single one fails the build (QG-1).
- False splits ≤ 5% of the corpus's merge-labelled pairs.
- Fingerprints match the 50 frozen expectations byte for byte, under three cultures (QG-2).
- Two consumers racing on one fingerprint produce one job and two aliases, with no exception.
- Every job has at least one alias, including the posting that created it (AC-08).
- `fingerprint_version` is stamped on every row.

## Links

[[../adr/0001-conservative-fingerprint|ADR-F2-0001]] · [[../test-plan]] §The dedup corpus
