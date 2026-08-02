# T07 — Notes

**Layer:** app · **Deps:** T04 · **Est:** S · **Owner:** Viacheslav

## What

Attach free-text notes to an application, from Telegram or the API, appearing in the
history view. Notes are capped and **never logged** — only their length is, because a note may
contain anything the Owner typed.

## Done when

- A note is stored with its time and appears in the history view (AC-06).
- Notes over 4 000 characters are refused with a clear message.
- Note content never appears in a log line or a span attribute — asserted by a scan test.
- Adding a note updates `last_activity_at`, so it counts as engagement for reminders.
- Notes are owner-scoped like everything else in this feature.

## Links

[[../data-model]] §application_notes
