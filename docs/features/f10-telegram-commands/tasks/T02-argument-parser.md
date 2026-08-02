# T02 — Argument parser

**Layer:** app · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

Forgiving positional parsing plus `key:value` inline filters, per
[[../contracts/command-catalogue|the catalogue]] §Argument parsing. Arguments become typed values;
they are never concatenated into a query or filter expression.

## Done when

- `/company stripe`, `/company Stripe` and `/company stripe.com` all resolve to the same company.
- A missing required argument enters the multi-step flow rather than returning an error (catalogue §Argument parsing).
- A malformed value names what was wrong and shows the usage line.
- An unknown inline filter is treated as search text, with a note.
- Quoted phrases survive as single terms; duplicate filters are deduplicated.
- A test asserts no parsed value reaches a query as raw concatenated text.

## Links

[[../contracts/command-catalogue|catalogue]] §Argument parsing
