# T06 — MarkdownV2 escaping and formatters

**Layer:** telegram · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

`MarkdownV2Escaper`, `DigestHeaderFormatter` and `CardFormatter` per
[[../contracts/telegram-messages|the contract]]. The escaper is the single path to a message, enforced
by an architecture test that forbids interpolating a non-constant directly into message text.

## Done when

- Every character requiring escape in MarkdownV2 is escaped; the hostile-input set renders safely.
- Truncation counts graphemes, not bytes — a flag emoji at the boundary is not split.
- The header is six lines or fewer in every variant (AC-01).
- An architecture test forbids raw interpolation into message text.
- Every layout in the contract has a committed snapshot, reviewed in diffs.

## Links

[[../contracts/telegram-messages|contract]] · [[../test-plan]] §The rendering corpus
