# T09 — Telegram search command

**Layer:** telegram · **Deps:** T03 · **Est:** S · **Owner:** Viacheslav

## What

`/search <query>` in the bot, rendering results in the same card layout as the digest so
there is one visual language across the product.

## Done when

- Results render in the digest card form (AC-11).
- Filters can be expressed inline in the query in a simple documented syntax.
- No results produces a helpful message suggesting a broader query, not an empty response.
- An unavailable index produces a clear message and does not affect other commands.
- Results are capped at 10 with a count of the total found.

## Links

[[../../f5-daily-digest-telegram/contracts/telegram-messages|F5 message contract]]
