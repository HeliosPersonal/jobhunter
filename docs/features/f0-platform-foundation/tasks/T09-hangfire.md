# T09 — Hangfire on PostgreSQL

**Layer:** infra/jobs · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

Hangfire with `Hangfire.PostgreSql` in the same database under the `hangfire` schema.
Server hosted in `JobHunter.Worker` only. Dashboard mapped but cluster-internal and gated on the
`jobhunter:admin` scope. Cron expressions declared in `Europe/Kyiv`, not UTC. Add a
`RecurringJobRegistry` so later features register a schedule by adding one line in their own
`DependencyInjection.cs`.

## Done when

- A recurring job survives a process restart and fires on its next scheduled occurrence.
- Schedules are declared in `Europe/Kyiv`; a DST-transition test asserts 07:00 stays 07:00.
- The dashboard is not reachable through the ingress and requires the admin scope.
- Two worker instances started accidentally result in one recurring-job owner (distributed lock), asserted by test.
- A later feature adds a schedule without editing any F0 file.

## Out of scope

- Actual schedules — F1 adds discovery, F3 adds the daily Run.

## Links

[[../../../00-overview/adr/0004-hangfire-scheduling|ADR-0004]] · [[../sad]] §6
