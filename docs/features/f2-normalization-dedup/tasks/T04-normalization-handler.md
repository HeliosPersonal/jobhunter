# T04 — Per-provider normalizers and the normalisation handler

**Layer:** app · **Deps:** T02, T03 · **Est:** M · **Owner:** Viacheslav

## What

One `IPostingNormalizer` per `AtsKind` for provider-specific field extraction, then the
shared normalisers. `NormalizationHandler` consumes `RawPostingIngested`, produces a candidate `Job`
and publishes `JobNormalized`. A payload missing a required field records a failure against the raw
posting and does not halt the batch.

## Done when

- Every provider fixture from F1 normalises to a complete canonical job (AC-01).
- A payload missing title or apply URL records a normalisation failure with a reason and does not throw (AC-04).
- One failing posting does not prevent the others in the same batch from normalising.
- The handler is idempotent on the raw posting id.
- Normalisation is a pure function — a test asserts no clock, no randomness and no I/O.

## Links

[[../sad]] §6.1 · [[../../../architecture/event-catalog]]
