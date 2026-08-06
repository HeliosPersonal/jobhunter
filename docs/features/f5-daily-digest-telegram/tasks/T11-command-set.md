# T11 — Command set

**Layer:** telegram · **Deps:** T10 · **Est:** M · **Owner:** Viacheslav

## What

F5 T11 ships **seven** commands, of which `/start`, `/help`, `/digest` are the **bootstrap subset**
that must ship with the first digest so the bot is usable on its own. The seven are `/start`,
`/help`, `/digest`, `/saved`, `/pipeline`, `/search`, `/stats`.

**Ownership** ([[../../../AUDIT-RESOLUTION-DECISIONS|§8]]): F5 owns `/start`, `/help`, `/digest`,
`/saved` and `/stats` (handlers live here). `/pipeline` (F6) and `/search` (F9) are **registered**
against F10's registry, not implemented here — F5 wires placeholders that degrade gracefully until
F6/F9 exist. `/stats` is **retained**, never dropped.

`/digest` re-renders from stored state and **must not touch the delivery log** — re-rendering and
re-delivering are different operations and conflating them would re-send the morning's cards.

## Done when

- Every command returns output in the same scannable card form as the digest (AC-12).
- `/digest` re-renders without writing delivery-log rows and without re-sending through the delivery path.
- `/start` from an unauthorised chat produces no confirmation — only a log entry.
- An unknown command returns one line plus the help list; there is no conversational fallback and no LLM in the command path.
- `/search` and `/pipeline` degrade gracefully before F9 and F6 exist, saying so plainly.

## Implementation map

> Mechanical checklist. Copy the named exemplars; contested points resolved here per the docs.

**Ownership (fixed by AUDIT-RESOLUTION §8).** F5 *implements* handlers for `/start`, `/help`, `/digest`,
`/saved`, `/stats`. `/pipeline` (F6) and `/search` (F9) are **registered but not implemented** — F5 wires
placeholders that degrade gracefully ("Search isn't available yet" style), saying so plainly. `/search`
handler code already exists (`JobHunter.Telegram/Search/`, from F9) — reuse it; do not reimplement. The
full 22-command catalogue + registry is F10, not here.

**Routing exemplar.** Commands arrive as `update.Message.Text` starting `/`. Route in
`Transport/OwnerGatedUpdateProcessor.cs` (already the allowlist-gated entry point). Add a
`Commands/CommandRouter.cs` that maps the leading token to a handler; unknown → one-line "unknown
command" + the help list (**no conversational fallback, no LLM in the command path** — ADR-F10-0002).

**Files to create** (`src/JobHunter.Telegram/Commands/`)
- `CommandRouter.cs` — token → handler dispatch; unknown-command fallback; the `/help` list is derived
  from the registered set so it cannot drift.
- `StartCommandHandler.cs` — confirms chat id + whether authorised. **Unauthorised chat → no
  confirmation, only a log entry** (AC / contract). Reuse `Auth/OwnerAuthorizer.cs`.
- `DigestCommandHandler.cs` — **re-renders today's digest from stored state via `IDigestRenderer`;
  MUST NOT touch the delivery log and MUST NOT go through the delivery path** (conflating re-render with
  re-deliver would re-send the morning's cards). Load the digest, render, send directly.
- `SavedCommandHandler.cs` — saved roles newest-first, same card layout (reads the store T10 writes).
- `StatsCommandHandler.cs` — this week: delivered/opened/ignored/applied + precision trend (Dapper read
  model over `delivery_log` + signals). `/stats` is **retained, never dropped**.
- `PlaceholderCommandHandler.cs` — one class serving `/pipeline` and `/search`-before-F9, returning the
  graceful "not available yet" line. (If the F9 search handler is wired, `/search` uses it directly and
  only `/pipeline` is a placeholder.)

**Files to edit**
- `Transport/OwnerGatedUpdateProcessor.cs` — dispatch messages beginning with `/` to `CommandRouter`.
- `TelegramHostExtensions.cs` / `DependencyInjection` — register the handlers + router.

**Card layout reuse.** Every command's output is the **same scannable card form as the digest** (AC-12).
Reuse `Formatting/CardFormatter.cs` + `CardView.cs` — do not invent a second layout.

**Tests** (`tests/JobHunter.Telegram.Tests/Commands/`)
- Each command returns card-form output (AC-12) — snapshot against the rendering corpus where layout is
  involved.
- `/digest` re-renders **without writing delivery-log rows and without re-sending** — assert the fake
  delivery log is untouched and the delivery path is not entered.
- `/start` from an unauthorised chat → no confirmation, one log entry (assert via the log capture).
- Unknown command → one line + help list; assert **no LLM client is ever resolved/called** in the path.
- `/search` and `/pipeline` degrade gracefully before F9/F6 — assert the plain "not available" line.

**Gotchas:** no LLM in the command path (assert the client is never called, like invariant-6 tests); no
CV anywhere; structured logging only.

## Delivered

Shipped in five commits on `master`:

- `a182a14` — `CommandRouter` + `StartCommandHandler`, `HelpCommandHandler`, `PlaceholderCommandHandler`
  and the `SearchCommandAdapter`; the singleton-routes / scope-acts split (`ICommandDispatcher` /
  `ScopedCommandDispatcher` open a scope per command, the router and handlers are scoped) and the
  unknown-command fallback (one line + the derived help list, **no conversational fallback, no LLM** —
  ADR-F10-0002).
- `7e65651` — `/digest` re-renders today's digest through `IDigestRenderer` **without touching the delivery
  log and without re-sending** through the delivery path.
- `f69e630` — `/saved`: `ISavedRolesQuery` read port + `SavedRolesQuery` Dapper impl (signals → jobs →
  companies → scores → matches), roles newest-first in the digest card layout.
- `8bede47` — `/stats`: `IWeeklyStatsQuery` read port + `WeeklyStatsQuery` Dapper impl over `delivery_log`
  + `signals`; the week window, precision and trend are computed in the handler against `IClock`.
- (this commit) — docs + tracker row to `done`.

All seven commands (`/start`, `/help`, `/digest`, `/saved`, `/stats`, `/pipeline`, `/search`) render through
the digest's `CardFormatter` / `CardView` (AC-12). The command path is deterministic: no LLM is resolved or
called, and no CV is read. The read ports are read-only (Dapper never writes — architecture rule 4).

**Deferred to T12:** there is **no production `IDigestRenderer` implementation yet** — only the port and
`/digest`'s use of it (unit-tested against a fake renderer). T12 owns the concrete renderer and the
rendering-corpus snapshots, after which `/digest` is live end-to-end.

## Links

[[../contracts/telegram-messages]] §Commands ·
[[../../f10-telegram-commands/adr/0002-no-conversational-fallback|ADR-F10-0002]]
