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

## Implementation map

> A mechanical checklist so implementation is execution, not investigation. Exemplars are named; copy
> them. Contested points are resolved here per the docs — do not re-litigate mid-task.

**Design decision — scheduling replaces the eager trigger (resolved per ADR-F5-0001).** Today assembly
fires on `RankingCompleted` and delivery fires immediately on `DigestReady` (T03/T08). ADR-F5-0001 keys
the digest on *"Run state at 06:45"* and makes 07:00 a hard slot, so both become **scheduled ticks**:

- **06:45 `DigestAssemblyDue`** → assembly runs against whatever state the Run is in. Assembly stays
  idempotent on `uq_digests_run`, so it is *assemble-if-absent*: if the happy path already assembled
  earlier via `RankingCompleted`, this is a no-op re-emit; otherwise it assembles the degraded variant.
- **07:00 `DigestDeliveryDue`** → delivery. `DeliveryHandler` moves from `Handle(DigestReady)` to
  `Handle(DigestDeliveryDue)`; it resolves today's Run (`IRunRepository.FindActiveRunAsync`, else most
  recent for the day), loads its digest, delivers. `DigestReady` no longer triggers delivery — remove
  that consumption path so nothing sends before 07:00. `DigestReady` remains as the assembled-signal.

**The "No Run at all" / FK question (resolved).** `digests.run_id` FKs `runs` (cascade), so an empty
digest needs a Run row. Wire a **02:00 `DailyRunDue`** tick (copy `DiscoveryCycleTrigger`) that creates
the day's Run in `Created` state. By 06:45 a Run row therefore always exists; "No Run at all" maps to a
`Created`/`Enriching` Run with zero scores → Empty/NothingNew digest, and the FK is satisfied. (True
absence of a Run row means the 02:00 tick itself never fired — that is the silence case ADR-F5-0001
hands to the R1 runbook alert, not a digest to render.)

**DigestMode derivation (new, Application side).** `DigestMode` (the enum) lives in `JobHunter.Telegram`
but the *classification* is the delivery layer's job. Add a pure mapper in Application —
`Reporting/DigestModeResolver.cs` — from `(RunState, cardCount, strongMatches, stillAnalysing)` per the
ADR table: `Reporting`/`Delivered`→`Full` (or `NothingNew` when zero cards & zero suppressed);
`Enriching`/`Matching`/`Created`→`Partial`; `CostAborted`→`BudgetReached`. Persist the resolved mode on
`Digest` (new field `Mode` + `CompaniesChecked`, `AnalysedCount`) so delivery renders from stored state,
not a live re-classification. Migration: `F5_AddDigestMode`.

**Files to create**
- `src/JobHunter.Application/Reporting/DigestAssemblyDue.cs` — copy `Discovery/DiscoveryCycleDue.cs`.
- `src/JobHunter.Application/Delivery/DigestDeliveryDue.cs` — same shape (`DateTimeOffset DueAt`).
- `src/JobHunter.Application/Discovery/DailyRunDue.cs` — same shape (02:00 run-start).
- `src/JobHunter.Application/Reporting/DigestModeResolver.cs` — pure `static DigestMode Resolve(...)`.
- `src/JobHunter.Infrastructure/Scheduling/DigestAssemblyTrigger.cs` — copy `DiscoveryCycleTrigger.cs`.
- `src/JobHunter.Infrastructure/Scheduling/DigestDeliveryTrigger.cs` — same.
- `src/JobHunter.Infrastructure/Scheduling/DailyRunTrigger.cs` — same.

**Files to edit**
- `src/JobHunter.Infrastructure/DependencyInjection.cs` `AddDiscovery` — register the three triggers
  (`AddScoped`) + three `RecurringJobBinding`s + three cron consts, exactly like `DiscoveryCycleJobId`.
  Crons (Kyiv, applied via `RecurringJobRegistry.Kyiv`): `DailyRun "0 2 * * *"`,
  `DigestAssembly "45 6 * * *"`, `DigestDelivery "0 7 * * *"`.
- `src/JobHunter.Application/Reporting/DigestAssembler.cs` — add a `Handle(DigestAssemblyDue)` overload
  that resolves today's Run and calls the existing assembly body with `run.State` branching + the
  resolved `DigestMode`; keep the `RankingCompleted` path as the early/happy trigger.
- `src/JobHunter.Application/Delivery/DeliveryHandler.cs` — switch consumed message to
  `DigestDeliveryDue`; resolve Run→digest instead of reading `message.RunId`.
- `src/JobHunter.Domain/Reporting/Digest.cs` — add `Mode`, `CompaniesChecked`, `AnalysedCount`; thread
  through the constructor guard.
- `src/JobHunter.Infrastructure/Persistence/Reporting/DigestConfiguration.cs` + new migration.

**Tests to write** (extend existing files; do not create parallel suites)
- `tests/JobHunter.Application.Tests/Reporting/DigestAssemblerTests.cs` — four degraded paths, one per
  ADR row, each asserting the resolved `DigestMode` and a persisted digest. Reuse `RankingCompletedRun`.
- `tests/JobHunter.Application.Tests/Reporting/DigestModeResolverTests.cs` — pure table test, one case
  per `(RunState, counts)` combination incl. zero-cards→`NothingNew` vs `Partial`.
- `tests/JobHunter.Application.Tests/Delivery/DeliveryHandlerTests.cs` — retarget to `DigestDeliveryDue`.
- `tests/JobHunter.Infrastructure.Tests/RecurringJobApplierTests.cs` — assert the three new job ids are
  installed with the right crons in `Kyiv`. **DST:** interpret each cron through `RecurringJobRegistry.Kyiv`
  across the March and October transition dates (pass fixed instants; `Cronos` is internal to Hangfire —
  use `TimeZoneInfo` + the registry's own interpretation, not a direct `Cronos` reference) and assert the
  07:00-Kyiv occurrence lands at the correct UTC offset on both sides. This is the "±3 min across both DST
  transitions" criterion.

**Snapshots** (the "committed rendering snapshot per variant" criterion) — the four header shapes already
exist in `DigestHeaderFormatter` (T06) and are snapshotted under
`tests/JobHunter.Telegram.Tests/Fixtures/rendering-corpus/`. Confirm one snapshot per `DigestMode` exists
and is referenced by `RenderingCorpusSnapshotTests`; add any missing variant. No new formatter code.

**Gotchas:** suppress `CA2012` around NSubstitute `.Returns` on `ValueTask` arranges; return concrete
`List<T>` (CA1859); the 02:00/06:45/07:00 bindings only install where `hangfire.EnableServer` (Worker).

## Links

[[../adr/0001-never-delay-the-digest|ADR-F5-0001]] · [[../contracts/telegram-messages]] §Degraded-day variants
