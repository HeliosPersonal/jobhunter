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

## Delivered

The `jobhunter-telegram` host now carries the full bot transport, wired in one
`[ExcludeFromCodeCoverage]` extension (`AddJobHunterTelegramBot`):

- **`INotifier` port + value types** — `INotifier.SendAsync(chatId, RenderedMessage, ct)` in
  `Domain/Abstractions`, with `RenderedMessage`/`InlineButton` in `Domain/Notifications`. The one
  outbound-message contract; the CV never crosses it.
- **`OwnerAuthorizer`** — a `FrozenSet<long>` allowlist (ADR-0014, invariant 9). `IsOwner` is a pure
  membership test; a rejected chat id is logged once at **warning** level and nothing else about the
  update is touched (AC-10). `OwnerGatedUpdateProcessor` fronts routing with it — an update with no
  chat, or from a non-Owner chat, is dropped before any handler runs.
- **`TelegramSendPacer`** — lock-guarded slot reservation over `IClock`, spacing sends by
  `MinSendInterval` (the 30 msg/s limit) and deferring by a 429's `retry_after`. A penalty never
  shortens an existing longer wait, so a provider cool-off is honoured **exactly**. Pure arithmetic,
  asserted entirely over `FakeClock` — no test waits on real time.
- **`TelegramNotifier : INotifier`** — paced, 429-aware `sendMessage`. The bot token rides only on
  the injected `HttpClient.BaseAddress` (`.../bot{token}/`); the adapter builds token-free relative
  paths and never logs, throws or spans the token (invariant 12). `retry_after` is read from the JSON
  body, falling back to the `Retry-After` header, then a 1 s default. A send exhausting its attempt
  budget or refused is a `TelegramSendException` — never a silent drop. A delay seam
  (`Func<TimeSpan, CancellationToken, Task>`) keeps the retry timing test-observable.
- **`TelegramLongPollService`** — a single-consumer `BackgroundService`: `getUpdates` with a long
  timeout, each update handed to the processor **before** its id advances the acknowledged offset (a
  crash mid-batch reprocesses rather than skips), and a transient poll fault logged and retried after
  `ReconnectDelay` so the loop never dies and reconnects without losing updates.
- **Single replica, `Recreate`** — `k8s/base/telegram/deployment.yaml` pins one replica with
  `strategy: type: Recreate`; a manifest test asserts both (two long-poll consumers would each get
  half the updates).

Options bind and validate at startup (`.Validate().ValidateOnStart()`): a bot token is required and
the allowlist must be non-empty. Internal interfaces are exercised with hand-rolled spies (NSubstitute
cannot proxy an internal interface without `[InternalsVisibleTo("DynamicProxyGenAssembly2")]`, which no
project opts into). JobHunter.Telegram coverage: **97.8% line / 92.6% branch**.

## Links

[[../../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]] · [[../sad]] §7
