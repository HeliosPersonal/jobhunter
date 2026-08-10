# T08 — Delivery handler with per-card idempotence

**Layer:** app · **Deps:** T02, T06, T07 · **Est:** L · **Owner:** Viacheslav

## What

`DeliveryHandler` consuming `DigestDeliveryDue` (the 07:00 slot): load the digest, load already-delivered
card keys, send only the remainder, and write a delivery-log row **immediately after each successful send**
([[../adr/0002-delivery-idempotence|ADR-F5-0002]]).

## Done when

- A clean delivery of 10 cards produces 12 messages and 12 log rows.
- Killing delivery after card 3 and restarting sends exactly the remaining 7 — no card twice (AC-04, QG-2).
- Retrying a completed delivery sends nothing.
- Two racing handlers cannot double-send; the unique constraint arbitrates.
- A 400 on one card logs that card as failed and delivers the rest.
- Every case in [[../test-plan|test-plan]] §The duplicate-delivery suite passes.

## Delivered

- **`IDigestRenderer`** (a Domain port) + **`RenderableMessage`** (a `(CardKey, RenderedMessage)` pair):
  rendering crosses a port because the T06 formatters and the `INotifier` implementation live in an adapter
  the Application-layer handler cannot reference (the arch arrow runs the adapters → Application, never the
  reverse). That adapter is `JobHunter.Telegram.Transport`, composed by both the Worker (which actually runs
  `DeliveryHandler`, off its 07:00 Hangfire cron) and the bot host (Task #88); the handler owns pure,
  idempotent orchestration and is tested against fakes.
- **`DeliveryHandler`** (`JobHunter.Application/Delivery`) consuming `DigestDeliveryDue` (the Worker's 07:00
  Hangfire tick, not `DigestReady` — which is a no-consumer assembled marker): loads the persisted
  digest (a missing one is surfaced so Wolverine redelivers once the write is visible), renders the ordered
  keyed sequence, loads the already-delivered keys for `(run, chat)`, and sends only the remainder —
  writing a `delivery_log` row **immediately after each successful send** (send-then-log, ADR-F5-0002).
- **`NotificationRejectedException`** distinguishes a permanent per-card refusal (a Telegram 400) from a
  transient fault: the handler catches the rejection, logs that one card failed and delivers the rest
  (AC-05), while a transient fault propagates so delivery resumes from the log. `TelegramNotifier` now
  raises the rejection on a non-429 4xx and a `TelegramSendException` on a 5xx.
- **`DigestDelivered`** integration event (`RunId`, `ChatId`, `MessagesSent`, `CardsFailed`), keyed on
  `(RunId, ChatId)` and re-emitted on a replay with a zero send count.
- **`DeliveryOptions.OwnerChatId`**, startup-validated non-zero, is the `chat_id` half of the idempotence key.
- **Tests** (`DeliveryHandlerTests`, 20 cases, zero-database/zero-network) cover the full duplicate-delivery
  suite: clean 10-card delivery → 12 sends / 12 rows; kill-after-3 → exactly 7 remaining, no card twice;
  crash in the send→log window → one card re-sent (at-least-once); retry after success → nothing sent;
  two racing handlers → one row per key (the unique constraint arbitrates); a 400 → that card logged failed
  and the rest delivered (and re-deliverable later since no row is written); a transient fault propagates.
  `DeliveryHandler` is 100% line / 100% branch.

## Links

[[../adr/0002-delivery-idempotence|ADR-F5-0002]] · [[../sad]] §10 QG-2
