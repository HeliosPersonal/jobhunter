# F5 T09 — Delivery scheduling and degraded-day variants

**Status:** Approved (design)
**Date:** 2026-08-05
**Task:** [[../../features/f5-daily-digest-telegram/tasks/T09-schedule-degraded]]
**ADR:** [[../../features/f5-daily-digest-telegram/adr/0001-never-delay-the-digest|ADR-F5-0001]]

## Problem

The F5 digest pipeline today is **event-driven**: `RankingCompleted → DigestAssembler → DigestReady →
DeliveryHandler` delivers **immediately**. Two consequences violate the feature's contract:

1. **No 07:00 gate.** A Run that finishes ranking at 05:00 delivers at 05:00, not 07:00. QG-1 requires
   delivery at **07:00 ±3 min Europe/Kyiv**, across both DST transitions.
2. **Three degraded paths never ship a digest.** Assembly only fires on `RankingCompleted`. A Run that
   is still `Enriching`/`Matching` at 06:45, a `CostAborted` Run (which never reaches ranking — `RunCostAborted`
   is published but no handler turns it into a digest), and the "no Run at all" case all currently deliver
   **silence** — the worst outcome per ADR-F5-0001.

T09 introduces the **06:45 assembly** and **07:00 delivery** schedules and the four degraded-day paths, so
that **every path produces a digest**.

## Constraints (from the accepted docs)

- ADR-F5-0001: 07:00 is a hard commitment; ship partial rather than late. Every Run state at 06:45 maps to a
  digest. Silence is never an outcome.
- SAD §6.3: `Hangfire (06:45) → DigestAssemblyDue`; a 07:00 delivery reads persisted state.
- SAD §8: all schedules in `Europe/Kyiv`; DST asserted by test.
- Invariant 8 / QG-2: delivery idempotent on `(run_id, chat_id, card_key)`. Unchanged.
- Coding standards: `IClock` everywhere; enums persist as `text`; options validate at startup;
  one `DependencyInjection.cs` per project; structured logging only.
- No F0 file edited — schedules register through the `RecurringJobRegistry`/`RecurringJobBinding` seam.

## Decisions (settled with the Owner)

1. **Delivery gate:** decouple via a new `DigestDeliveryDue` scheduled event. `DeliveryHandler` consumes it
   instead of `DigestReady`. `DigestReady` becomes a pure "assembled & persisted" marker.
2. **No-Run path:** handled at delivery, **date-keyed** — no synthetic Run or Digest is created. The delivery
   log stores the idempotency row with a null `run_id`.
3. **Renderer:** deferred. T09 persists the degraded-mode classification and wires the schedules/delivery;
   the production `IDigestRenderer` (job facts + inline keyboards + no-run empty message) lands in T10/T12.
   Delivery tests continue to use `FakeDigestRenderer`.
4. **Empty-path store:** reuse `delivery_log` — make `run_id` nullable and add a partial unique index for the
   null-run case. One idempotency mechanism, no second concept.

## Design

### 1. Scheduled trigger events (Contracts)

Two new `IIntegrationEvent` records in `JobHunter.Contracts/Pipeline/ReportingEvents.cs`, each a clock tick
carrying only `OccurredAt` (no RunId — the handler resolves the day's Run):

```csharp
public sealed record DigestAssemblyDue(DateTimeOffset OccurredAt) : IIntegrationEvent;
public sealed record DigestDeliveryDue(DateTimeOffset OccurredAt) : IIntegrationEvent;
```

Both registered in `PipelineEventContext` (`[JsonSerializable(...)]`).

### 2. Two Hangfire schedules (Infrastructure)

New `AddReporting(services, configuration)` method in `Infrastructure/DependencyInjection.cs`, mirroring
`AddDiscovery`: gated on `HangfireOptions.EnableServer`, registered through `RecurringJobBinding`, applied by
the existing `RecurringJobApplier`. Two thin trigger bodies mirroring `DiscoveryCycleTrigger`:

- `DigestAssemblyTrigger.PublishAsync()` → publishes `DigestAssemblyDue(clock.UtcNow)`.
- `DigestDeliveryTrigger.PublishAsync()` → publishes `DigestDeliveryDue(clock.UtcNow)`.

Crons, in `Europe/Kyiv`:

- `digest-assembly` = `45 6 * * *`
- `digest-delivery` = `0 7 * * *`

The 15-minute gap absorbs assembly + apply-link verification (ADR-F5-0001).

### 3. Degraded-mode classification, persisted on the Digest (Domain + migration)

The variant must survive to 07:00 so delivery is a replay of stored state (S2), not a recomputation. Add a
**domain** `DigestMode` enum to `JobHunter.Domain/Reporting/` (distinct from the Telegram render-time enum,
which T10's renderer maps onto):

```csharp
public enum DigestMode { Full, NothingNew, Partial, BudgetReached }
```

Add a `Mode` property to the `Digest` aggregate (constructor parameter, persisted as `text`). Migration
`F5AddDigestMode` adds `digests.mode text not null default 'Full'`.

### 4. Time-triggered assembly (Application)

New entry `Handle(DigestAssemblyDue, IMessageBus, CancellationToken)` on `DigestAssembler`. It resolves the
day's Run via `IRunRepository.FindActiveRunAsync`, then:

| Run state at 06:45 | Action |
|---|---|
| `Ranking`/`Researching`/`Reporting`/`Delivered` | a digest already exists from `RankingCompleted` → find it, re-publish `DigestReady`, no new write (idempotent) |
| `Enriching`/`Matching` | assemble from whatever completed → **Partial**; record carried-over count |
| `CostAborted` | assemble reduced digest → **BudgetReached** |
| `Created`/`Failed` | assemble an empty digest for the Run → **NothingNew** (no candidates exist yet / the night broke) |
| no active Run | **no-op** — the empty case is handled at delivery (decision 2) |

The existing `RankingCompleted` handler is extended only to set `Mode` (`NothingNew` when zero cards and zero
suppressed, else `Full`). The assembly internals (`SelectCandidates`, `VerifyApplyLinksAsync`, `Assemble`,
suppression breakdown, narrative) are **reused unchanged**; only the entry point and the `Mode` argument are
new. One digest per Run stays a DB constraint, so a `DigestAssemblyDue` after a `RankingCompleted` already
produced the digest is a no-op re-publish.

### 5. Delivery gated to 07:00 (Application)

`DeliveryHandler.Handle` switches its trigger from `DigestReady` to `DigestDeliveryDue`. Because the tick
carries no RunId, it resolves the day's target:

1. `IRunRepository.FindActiveRunAsync` (falls back to most-recent if the active Run is already `Delivered`).
2. `IDigestRepository.FindByRunAsync(runId)`.
3. **If a digest is found:** the existing send loop runs unchanged — render, read `DeliveredKeysAsync`, send
   the remainder, record per card, publish `DigestDelivered`. QG-2 idempotence is untouched.
4. **If no Run / no digest:** send one standalone empty message (the "nothing new / everything is working"
   text), recording a single delivery-log row with `run_id = null` and
   `card_key = "empty-digest:{yyyy-MM-dd in Kyiv}"`.

`DigestReady` is no longer consumed by delivery. It stays published by the assembler as the "persisted"
marker (observability, future consumers).

### 6. No-Run empty idempotency (Infrastructure + migration)

Migration `F5DeliveryLogNullableRun`:

- `delivery_log.run_id` → nullable; the existing FK stays (a null FK column is allowed).
- New partial unique index `uq_delivery_log_empty` on `(chat_id, card_key) WHERE run_id IS NULL`, so a second
  07:00 tick on the same day (retry/replay) inserts nothing.

`IDeliveryLog` gains an overload for the null-run case (or `DeliveryRecord.RunId` becomes `Guid?`); the raw
`ON CONFLICT` upsert branches on whether `run_id` is null to target the right constraint. `DeliveryRecord`
(domain) allows a null `RunId` only for the empty-digest key.

## Components and boundaries

| Unit | Layer | Responsibility | Depends on |
|---|---|---|---|
| `DigestAssemblyDue`, `DigestDeliveryDue` | Contracts | scheduled tick events | nothing |
| `DigestAssemblyTrigger`, `DigestDeliveryTrigger` | Infrastructure | publish one tick on cron | `IMessageBus`, `IClock` |
| `AddReporting` bindings | Infrastructure | register the two crons (Kyiv, EnableServer-gated) | `RecurringJobBinding` |
| `DigestMode` (domain) + `Digest.Mode` | Domain | persisted variant | nothing |
| `DigestAssembler.Handle(DigestAssemblyDue)` | Application | classify Run state → assemble a digest each path | `IRunRepository`, existing assembler internals |
| `DeliveryHandler.Handle(DigestDeliveryDue)` | Application | resolve day's digest, deliver, or empty path | `IRunRepository`, `IDigestRepository`, `IDeliveryLog`, `INotifier` |
| migrations | Infrastructure | `digests.mode`; `delivery_log.run_id` nullable + partial index | — |

## Testing

- **RecurringJobRegistry / crons:** `45 6 * * *` and `0 7 * * *` register under the right ids; both read in
  `Kyiv`. Trigger bodies publish the right event with the clock's instant.
- **DST test (QG-1):** using a fixed `FakeClock`, assert the Kyiv-interpreted 07:00 maps to the correct UTC
  instant on both sides of the 2026 spring-forward and fall-back transitions (07:00 stays 07:00 local).
- **Assembler degraded paths:** `Enriching`/`Matching` → `Partial` with carried-over; `CostAborted` →
  `BudgetReached`; a completed Run → reuse/no-write; no active Run → no-op. Each asserts the persisted `Mode`
  and the counts. Zero-database unit tests with substitutes.
- **Delivery paths:** digest found → existing loop (all T08 idempotence tests stay green, retargeted to
  `DigestDeliveryDue`); no Run → one empty message, second tick same day sends nothing (date idempotency).
- **Rendering corpus:** the four committed header snapshots (full / nothing-new / partial / budget-reached)
  stay green — no new snapshots needed, since the formatters (T06) already cover every variant.
- **Migrations gate:** both migrations apply on a clean database via `TestDatabase` in every integration test.

## Scope and risk

Spans Contracts + Domain + Application + Infrastructure + two migrations + tests. It is a full **M** and may
nudge the ≤500-LOC guide; deferring the renderer keeps it contained. If it clearly overruns, the empty-path
(no-Run) slice is the natural split point. Behaviour docs already describe every path (ADR-F5-0001, contract
§Degraded-day variants), so no doc changes beyond the tracker row.

## Non-goals

- The production `IDigestRenderer` (T10/T12).
- Callback/action handling, the command set (T10/T11).
- Near-duplicate grouping (T13).
- Any change to the T08 per-card idempotence mechanism or the send loop itself.
