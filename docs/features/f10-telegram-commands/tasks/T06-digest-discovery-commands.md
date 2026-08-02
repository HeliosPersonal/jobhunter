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

## Links

[[../contracts/command-catalogue|catalogue]] §Digest and discovery · [[../../f9-search-and-api/index|F9]]
