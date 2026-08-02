---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, feature/f5-daily-digest-telegram, mvp, jobhunter]
---

# Telegram message contract

> Layout, callback payloads, escaping rules and the command set. Every layout here has a case in the
> rendering corpus.

## Header — the three-second message

Six lines maximum. If the Owner reads nothing else, this must be enough to decide.

```
🌅 *Good morning\.*

*127* new · *9* strong matches · avg *185k USD*

🏆 *Staff Backend Engineer* — Snowflake · *95*
   Kafka · Azure · distributed systems

_9 cards below\. 34 hidden \(salary floor, timezone\)\._
```

Design decisions embedded here:

- **Counts first.** "127 new, 9 strong" is the whole decision for most mornings.
- **The best opportunity is in the header**, not the first card. If there is exactly one thing worth
  seeing, it is above the fold.
- **The hidden count is in the header, not buried in a footer.** This is [[../../../DECISION-LOG|D7]]
  made visible: ignoring teaches the system, and the Owner should see that it worked before they see
  anything else.
- Every `.`, `(`, `)`, `-`, `!` is escaped — MarkdownV2 requires it and an unescaped one silently
  fails the whole send.

## Card

```
*Senior Platform Engineer*
Stripe · Series\-public · Dublin / Remote EMEA
💰 150–190k EUR \(est, med conf\) · 🎯 *87*

• 7 yrs Kafka against a role naming Kafka as core
• Contractor\-friendly, B2B stated explicitly
• EMEA timezone, no overlap requirement

[ Open ] [ Ignore ] [ Save ] [ Applied ]
```

| Element | Rule |
|---|---|
| Title | Bold, truncated to 60 chars at a word boundary |
| Company line | Company · stage · location summary; stage omitted if `Unknown` |
| Salary | Published if available; otherwise the estimate marked `(est)` with its confidence band. **Never presented as fact when it is an estimate** |
| Score | The final score, whole number |
| Reasons | Exactly three, each ≤ 90 chars, from the match reasons — the ranking's own explanation, not a summary |
| Buttons | Always four, always in this order — muscle memory matters more than context-sensitivity |

Cards are sent as separate messages rather than one long one, so that editing a keyboard after a tap
affects only that card.

## Footer

```
─────────────
34 hidden: 21 below salary floor · 9 timezone · 4 employment type
2 jobs still processing — they'll be in tomorrow's digest
⚠️ 1 source degraded: greenhouse \(quarantined\)
```

The footer only appears when it has something to say. Lines two and three are omitted when zero.

## Degraded-day variants

Every one of these still arrives at 07:00 ([[../adr/0001-never-delay-the-digest|ADR-F5-0001]]).

**Nothing new** (AC-05)

```
🌅 *Good morning\.*

No new roles today\. 340 companies checked, nothing matched\.

_This is normal on a Monday\. Everything is working\._
```

The second line matters: an empty digest that does not explain itself is indistinguishable from a
broken one, and the Owner will spend a minute checking.

**Analysis incomplete** (AC-06)

```
🌅 *Good morning\.* \(partial\)

*84* new · *5* strong matches

_43 roles are still being analysed\. They'll appear tomorrow\._
```

**Budget reached** (AC-06, cost abort)

```
🌅 *Good morning\.* \(reduced\)

*127* new · *3* analysed before the daily budget was reached

_Raise the ceiling or reduce the company list\. Nothing was lost\._
```

## Callback payloads

Telegram caps `callback_data` at 64 bytes, so the payload cannot carry a UUID and an action name.

```
{action}:{shortId}

action  ∈ open | ign | sav | app        (3 chars)
shortId = base64url(HMAC-SHA256(cardKey, botSecret)[0..8])   (11 chars)

Total: 15 bytes.
```

The HMAC means a payload cannot be forged by guessing a card key, and the short id resolves through
`digest_cards` — so an id that no longer resolves produces a clear message rather than a silent
no-op (AC-09).

Acknowledgements, all under one second (QG-3):

| Action | `answerCallbackQuery` text | Keyboard after |
|---|---|---|
| Open | *(none — the URL button opens directly)* | unchanged |
| Ignore | `Won't show similar` | `[ Ignored ]` only |
| Save | `Saved` | `[ Open ] [ Saved ✓ ] [ Applied ]` |
| Applied | `Marked as applied` | `[ Open ] [ Applied ✓ ]` |

`Won't show similar` is deliberate phrasing. It tells the Owner their tap taught the system, which is
the retention mechanism from [[../../../DECISION-LOG|D7]]. `Ignored` alone would be a dead end.

## Commands

| Command | Behaviour |
|---|---|
| `/start` | Confirms the chat id and states whether it is authorised. An unauthorised chat gets no confirmation — only the log records it |
| `/digest` | Re-sends today's digest from stored state. Does **not** re-deliver — it renders the same digest fresh, without touching the delivery log |
| `/saved` | Saved roles, newest first, same card layout |
| `/pipeline` | Applications grouped by status (F6) |
| `/search <query>` | Typesense search results in card layout (F9) |
| `/stats` | This week: delivered, opened, ignored, applied; precision trend |
| `/help` | The above list |

Anything else gets a one-line "unknown command" and the help list. There is no conversational fallback
and no LLM in the command path — a bot that tries to chat sets an expectation this product does not
meet ([[../../f10-telegram-commands/adr/0002-no-conversational-fallback|ADR-F10-0002]]).

> F5 T11 ships **seven** commands, of which `/start`, `/help`, `/digest` are the bootstrap subset that
> must ship with the first digest. F5 owns `/start`, `/help`, `/digest`, `/saved` and `/stats`;
> `/pipeline` (F6) and `/search` (F9) are registered against F10's registry, not implemented here. The
> full command catalogue (22 commands), the registry that keeps it honest, and the multi-step and
> confirmation flows are [[../../f10-telegram-commands/index|F10]] ([[../../../AUDIT-RESOLUTION-DECISIONS|§8]]).

## Escaping

Every dynamic value passes through `MarkdownV2Escaper.Escape`. The characters requiring escape are
`_ * [ ] ( ) ~ \` > # + - = | { } . !`.

An architecture test forbids string interpolation of a non-constant directly into message text; the
only path to a message is through the formatter. The rendering corpus includes deliberately hostile
input:

| Hostile input | Must render safely |
|---|---|
| Title containing `*bold*` and `_italic_` | Escaped, displayed literally |
| Company name containing `[link](http://evil)` | Escaped, not a link |
| Description reason containing a newline and a backtick | Escaped, layout intact |
| Title of 400 characters | Truncated at a word boundary to 60 |
| Title in Arabic or Japanese | Renders; truncation counts graphemes, not bytes |
| Emoji in a company name | Passes through unbroken |
| Reason containing `\n\n` | Collapsed to a single space |

## Message limits

- 4096 characters per message. The formatter splits **at a card boundary**, never mid-card.
- 30 messages/second globally, 20/minute per chat. The sender paces to stay inside both and honours
  a `429` `retry_after` exactly.
- A ten-card digest is twelve messages (header, ten cards, footer) — comfortably inside the per-minute
  limit, but the pacing exists because `/digest` plus a large `/saved` can approach it.

## Related

[[../sad]] §8 · [[../test-plan]] §The rendering corpus · [[../../../DECISION-LOG|D7]]
