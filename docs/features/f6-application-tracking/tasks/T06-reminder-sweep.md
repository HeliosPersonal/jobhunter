# T06 — Reminder sweep

**Layer:** app · **Deps:** T04 · **Est:** M · **Owner:** Viacheslav

## What

A daily sweep at 08:00 Europe/Kyiv — deliberately an hour after the digest, so the
morning message stays about opportunities rather than admin. One reminder per condition, suppressed
until the condition clears or recurs.

## Done when

- A stale application produces exactly one reminder, asserted over seven consecutive simulated days (AC-05, QG-3).
- A reminder names the application and a suggested action, not just a fact.
- Acting on an application clears the condition and resets the threshold.
- Changing a threshold in configuration takes effect on the next sweep with no per-application rescheduling.
- The sweep is one indexed query over `next_action_at`, not a scan.
- Reminders are sent at 08:00, separate from the 07:00 digest.

## Links

[[../sad]] §6.2 · [[../PRD]] §8
