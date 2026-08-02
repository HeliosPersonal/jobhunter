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

## Links

[[../contracts/telegram-messages]] §Commands
