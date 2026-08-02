# T07 — Telegram host, allowlist and long polling

**Layer:** telegram · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

The `jobhunter-telegram` host: a long-poll hosted service, `OwnerAuthorizer` applied
**before routing**, and `TelegramNotifier` implementing `INotifier` with pacing and 429 handling.
Single replica with `Recreate` — two consumers would each receive half the updates, which presents as
randomly-ignored taps.

## Done when

- An update from an unauthorised chat is dropped before any handler runs, and the chat id is logged at warning level (AC-10).
- Sending paces to stay inside both rate limits and honours `retry_after` exactly.
- Long polling reconnects after a network interruption without losing updates.
- The manifest pins one replica with `Recreate`, asserted by a manifest test.
- The bot token never appears in a log, an exception message or a span.

## Links

[[../../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]] · [[../sad]] §7
