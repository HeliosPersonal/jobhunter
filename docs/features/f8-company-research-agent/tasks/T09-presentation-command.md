# T09 — Presentation and on-demand command

**Layer:** telegram/api · **Deps:** T08 · **Est:** M · **Owner:** Viacheslav

## What

Render the dossier in the digest card layout — warnings first, then categories, every
claim with its date and a link — plus the `/company` command and two owner-scoped endpoints.

## Done when

- Every rendered claim shows its observed date and links to its source (AC-02, AC-03).
- Warnings appear before other categories.
- Unavailable categories are stated, so absence is visible rather than ambiguous.
- `/company` returns a fresh dossier, or queues research and acknowledges (AC-05).
- An unknown company offers to add it to the registry rather than failing.
- Both endpoints are owner-scoped; without the scope they are refused (AC-09).
- All dynamic text is escaped through F5's escaper.

## Links

[[../../f5-daily-digest-telegram/contracts/telegram-messages|F5 message contract]]
