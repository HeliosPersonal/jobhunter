# T10 — Discovery cycle handler and fan-out

**Layer:** app · **Deps:** T02, T05 · **Est:** M · **Owner:** Viacheslav

## What

The six-hourly Hangfire schedule and `DiscoveryCycleHandler`: select due sources, emit
one `SourceFetchRequested` per source, and let `FetchSourceHandler` process them with bounded
concurrency. One message per source is what makes a single provider's failure a single message's
failure (QG-1).

## Done when

- Each due source is fetched exactly once per cycle (AC-01).
- Concurrency never exceeds the configured degree — asserted by a counting handler.
- Overlapping cycles skip sources already fetched recently rather than double-fetching.
- A source deleted while its message is in flight exits cleanly.
- The schedule is registered through F0's `RecurringJobRegistry` with no F0 file modified.

## Links

[[../sad]] §6.1 · [[../../f0-platform-foundation/tasks/T09-hangfire|F0 T09]]
