# T20 — Weekly precision@10 rating loop

**Layer:** app · **Deps:** T11, F5, F7-T03, F10 · **Est:** M · **Owner:** Viacheslav

## What

The measurement half of [[T11-golden-ranking|T11]], split out because it needs infrastructure the golden
set does not. A weekly Telegram prompt asks the Owner to rate the previous week's top cards; each rating
is recorded as an F7 Signal (so preference learning consumes it too) and aggregated into
`jobhunter.precision_at_10`, exported and charted. A baseline is captured at M4 so later improvement is
measurable rather than asserted.

This is the empirical counterpart to the golden set: the golden set proves the ranking is *stable* (a
change that breaks it fails the build); precision@10 measures whether it is *good* against the Owner's
real judgement.

## Done when

- A weekly job posts a rating prompt for the previous week's top-10 shown cards to the Owner's chat.
- Each rating is recorded as a Signal on the `signals` table, usable by F7 as well as by the metric.
- `jobhunter.precision_at_10` is computed from the ratings, exported and charted.
- A baseline is captured at M4 so later improvement is measurable rather than asserted.
- The prompt is idempotent: re-running the weekly job for a week already rated does not double-count.

## Blocked on

- **`signals` table** — [[../../../f7-preference-learning/tasks/T03-signal-capture|F7 T03]]: no Signal
  persistence exists yet.
- **The digest** — [[../../../f5-daily-digest-delivery/_epic|F5]]: there are no delivered cards to rate
  until the daily digest ships.
- **The command / callback surface** — [[../../../f10-telegram-command-surface/_epic|F10]]: the rating
  prompt is a Telegram interaction.

## Links

[[T11-golden-ranking|T11]] · [[../test-plan]] §The golden ranking set · [[../../../DECISION-LOG|D5]]
