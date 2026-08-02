# T09 — Delivery scheduling and degraded-day variants

**Layer:** app · **Deps:** T08, T05 · **Est:** M · **Owner:** Viacheslav

## What

The 06:45 assembly and 07:00 delivery schedules in `Europe/Kyiv`, plus the four
degraded-day paths from [[../adr/0001-never-delay-the-digest|ADR-F5-0001]]. **Every path produces a
digest** — silence is never an outcome.

## Done when

- Delivery lands at 07:00 ±3 min, asserted across both DST transitions.
- No new jobs still delivers a digest stating so plainly, and stating that nothing is wrong (AC-05).
- An incomplete Run delivers on time and names what is missing (AC-06).
- A cost-aborted Run delivers reduced with a visible warning and what to do about it (AC-06).
- No Run at all still delivers an empty digest rather than nothing.
- Each variant has a committed rendering snapshot.

## Links

[[../adr/0001-never-delay-the-digest|ADR-F5-0001]] · [[../contracts/telegram-messages]] §Degraded-day variants
