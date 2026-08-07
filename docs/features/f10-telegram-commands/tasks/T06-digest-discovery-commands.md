# T06 — Digest and discovery commands

**Layer:** telegram · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

`/digest`, `/more`, `/search`, `/hidden`. `/search` carries the inline-filter grammar;
`/hidden` is [[../../../CONTEXT]] invariant 11 made interactive — the footer gives the count, this
gives the jobs and the evidence.

## Done when

- `/digest` re-renders from stored state and writes **no** delivery-log rows.
- `/more` paginates the same stored digest rather than re-ranking, so ordering stays stable mid-morning.
- `/search` supports every filter in the catalogue and returns cards, count and facets (AC-02).
- `/hidden` lists suppressions grouped by reason with their evidence and a turn-off button (AC-04).
- An empty search suggests dropping the most restrictive filter rather than returning nothing.
- All output goes through F5's card formatter — no handler builds message text.

## Implementation

Four commands, each answering from a query service another feature already owns; F10 adds only the
Telegram-facing rendering, every message through F5's `CardFormatter`.

**`/digest` and `/more`.** `/digest` re-renders today's stored digest through the shared
`IDigestRenderer` (F5 T12) — it reads stored state and writes no delivery-log rows, so it cannot
re-send the morning's cards ([[../../f5-daily-digest-telegram/adr/0002-delivery-idempotence|ADR-F5-0002]]).
`/more [count]` paginates the **same frozen** below-the-cut set rather than re-ranking, so the ordering
stays stable mid-morning: it takes the next `count` cards (1–20, default 5, clamped with a note) below
today's cut in rank order and reports how many remain. Re-ranking here would make the sequence unstable
between taps, so it is deliberately a page, not a recompute.

**`/search <query>`.** `SearchCommandParser` is a **total** parser: every `key:value` token it
recognises becomes a typed filter, and any malformed or unknown token falls through to free text rather
than erroring (catalogue §Argument parsing). It covers the full catalogue grammar — `tech:`, `stage:`,
`country:`, `min:` (minimum score), `since:` (a relative window like `30d`/`2w`/`12h`), and `closed:`
(default excludes closed) — plus the `remote:`, `seniority:` and `min-salary:` supersets F9 already
indexes. `since:` is the one filter that needs a clock: `SearchCommandHandler` passes `IClock.UtcNow`
into `Parse`, which resolves the window to an absolute `PostedAfter` unix-seconds cutoff, so the domain
only ever sees an instant and `DateTime.UtcNow` never appears outside `SystemClock`
(architecture rule 5). `SearchFilterBuilder` turns `PostedAfter` into a `postedAt:>=` range clause. The
handler runs the typed query through the **shared** `ISearchQuery` port — the same service the API uses,
only the renderer differs (the O12 decision) — and `SearchResultRenderer` renders the top ten as digest
cards with the total-found count. It then surfaces the **leading facets** of the three refinable fields
(technologies→`tech:`, companyStage→`stage:`, countries→`country:`) as paste-back tokens, so the next
query can be narrower with no second round trip (AC-02). An empty result does not return nothing: with
active filters it names the single most restrictive one to drop (a numeric threshold cuts hardest, then
the date window, then the typed sets in catalogue order); with no filters it keeps the generic "broaden
your query" advice. A Typesense outage is a clear "search is unavailable" line, logged, degrading
`/search` alone (QG-3). No CV-derived value can appear — the results carry only the allowlisted
`JobDocument` (QG-2).

**`/hidden`.** Reads the latest Run's suppressed jobs through `IHiddenJobsQuery` (F7 T08 C5,
best-score first, each with its non-blank reason — invariant 11), groups them by reason in first-seen
order, and renders each reason as a bold "reason — count" header followed by its jobs as cards through
the one shared `CardFormatter`. F7 owns this handler; F10 only registers it against the catalogue (the
ownership table), so there is one implementation of "show what was hidden". Each reason-group header
carries a **"Turn this off"** inline callback button (AC-04, catalogue §/hidden), putting a wrong
learned weight one tap from switched off; the button's payload is the group's position as an opaque
token (`hoff:{index}`), never the reason text — a short id, not a fact (SAD §6.2). It reads only public
job facts and never the CV (the CV crosses exactly one boundary, and it is not this one). An empty
result is one plain, helpful line, never an empty message.

**Deferred to T10.** As with T03/T04/T05, this task ships the mechanism, not the live callback wiring.
The `/hidden` turn-off button carries its token but is not yet routed to the F7 disable-weight path
(that path is owned by T08's `/forget`, and the callback registry is wired in T10 alongside the dispatch
rewire against the full 22-command registry). The `since:`-clock injection and the shared-query renderer
are live; only the turn-off callback's route is deferred.

## Links

[[../contracts/command-catalogue|catalogue]] §Digest and discovery · [[../../f9-search-and-api/index|F9]]
