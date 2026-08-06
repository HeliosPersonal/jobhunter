---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-06"
feature_size: "L"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f5-daily-digest-telegram, mvp, jobhunter]
---

# Live-smoke checklist — one real digest to a test chat

> Manual, pre-release, executed **once** per meaningful change to the rendering or delivery path. It is
> **not a CI gate** — the [[test-plan#The rendering corpus|rendering corpus]] and the
> [[test-plan#The duplicate-delivery suite|duplicate-delivery suite]] cover everything that can be checked
> without a network. This checklist covers the one thing they cannot: that the four action buttons, the
> MarkdownV2 markup and the emoji actually render and work in a real Telegram client. Some things only break
> in the real app (T12 "Done when": *the four action buttons are verified working in a real Telegram client
> before M4 is called done*).

## Why this exists and stays manual

The rendering corpus asserts the exact bytes we send; it cannot assert that Telegram *accepts* those bytes,
that an inline keyboard lays out as one row of four, that a `MarkdownV2` escape we chose renders as the
literal character, or that a URL button opens the apply page. A single unescaped special silently fails the
whole send — and only the real client tells you which one. So this runs against a real bot token and a real
test chat, by a human, looking at a phone.

## Preconditions

- [ ] A **test** bot token (never the production token; never committed — from Infisical or a local
      user-secret, per invariant 12). The token lives only on the client base address, never in a log.
- [ ] A **test** chat id, added to `Telegram:AllowedChatIds` for the smoke run only.
- [ ] A digest exists in the store for a Run (a seeded or a real one), so `/digest` has something to render.
- [ ] Running the `JobHunter.Telegram` deployable pointed at the test bot and test chat.

## Steps

1. [ ] Send the day's digest to the test chat (trigger `DigestDeliveryDue`, or issue `/digest`).
2. [ ] **Header** — arrives, is at most six lines, reads cleanly (no stray backslashes, no `*` or `_`
       leaking as literal markup where prose was intended).
3. [ ] **Cards** — each card is its own message; title is bold; the `· ` separators, the `💰`/`🎯`/`🏆`
       emoji and the confidence band all render.
4. [ ] **Buttons** — every card shows exactly **four** buttons in one row, in the fixed order
       `[ Open ] [ Ignore ] [ Save ] [ Applied ]` (contract §Card).
5. [ ] **Open** — tapping opens the apply URL in the browser; it does not call back into the bot.
6. [ ] **Ignore** — tap acknowledges under a second with the toast `Won't show similar`; the keyboard
       collapses to `[ Ignored ]` only (contract §Callback payloads).
7. [ ] **Save** — tap acknowledges `Saved`; the keyboard becomes `[ Open ] [ Saved ✓ ] [ Applied ]`.
8. [ ] **Applied** — tap acknowledges `Marked as applied`; the keyboard becomes `[ Open ] [ Applied ✓ ]`.
9. [ ] **Footer** — appears only when there is something to say; the divider, the hidden breakdown and the
       `⚠️` degraded-source line render.
10. [ ] **Hostile input** — if a card in the sample carries markup, a link, an emoji or a non-Latin title,
        confirm it renders literally and the send did not fail.
11. [ ] **Re-read** — `/digest` re-renders the same digest without sending duplicates through the delivery
        path (it writes no delivery-log rows).

## Pass criteria

- [ ] Every message sent (nothing silently dropped by a MarkdownV2 escape error).
- [ ] The four buttons render and each action behaves as the table in [[telegram-messages#Callback payloads]].
- [ ] No CV text and no secret anywhere in the messages (the CV crosses exactly one boundary, and it is not
      this one).

## Execution record

| Date | Executed by | Build / commit | Result | Notes |
|---|---|---|---|---|
| _pending_ | Viacheslav | _to fill on the M4 pre-release run_ | _pending_ | First execution due before M4 is called done. |

> This table is updated in place each time the checklist is run; the row is the evidence that the one-time
> gate was met for a given release.

## Related

[[telegram-messages]] · [[../test-plan#Live smoke]] · [[../tasks/T12-rendering-corpus]]
