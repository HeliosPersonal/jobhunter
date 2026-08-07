# T09 — Operations commands

**Layer:** telegram · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

`/status`, `/cost`, `/sources`, `/run`, `/redeliver` — the chat replacement for the most
common runbook steps, so recovery does not need a terminal.

## Done when

- `/status` reports outcome, cost against ceiling, counts and degraded sources (AC-06) — [[../../../operations/runbooks|R1]]'s first question.
- `/cost` breaks spend down by stage and tier and flags estimate-vs-actual drift above 20%.
- `/sources` lists per-provider health with a release button for quarantined sources ([[../../../operations/runbooks|R4]]).
- `/run` is refused with an explanation when a Run is already live; the confirmation names the estimated cost.
- `/redeliver` states how many cards would actually be sent — usually zero, which is the point.
- All five are `Operator`-scoped and all three state-changing ones require confirmation.
- The runbooks are updated to reference these commands alongside the API endpoints.

## Implementation

Five commands that put the most common runbook steps (R1, R3, R4) in the chat, so recovery does not
need a terminal. Each behaviour is owned by the feature it belongs to; F10 adds the Telegram-facing
rendering and, for the three state-changing ones, the preview-then-confirm discipline the catalogue
mandates. All five are **Sensitive** (catalogue §Operations); none runs an LLM or touches the CV, and
every dynamic value reaches the reply through the one MarkdownV2 escaper.

**`/status`.** Answers R1's first question — *where did the last Run stop, and did it stay within
budget?* It reads the most recent Run through `IRunRepository`, its assembled digest counts through
`IDigestRepository`, and the day's degraded sources through `IDegradedCoverageQuery`, and states the
outcome, the spend against the ceiling, the counts and any degraded sources (AC-06). Read-only.

**`/cost [month]`.** Breaks the calendar month's spend down by pipeline stage and model tier, each line
carrying both the estimated and the actual dollars, and flags any line whose actual has drifted **more
than 20%** above its estimate — how a stale pricing table surfaces (R3). The month comes from an optional
`YYYY-MM` argument, else the current month from the injected clock; an unparseable argument is a business
outcome with a usage line, reading nothing. It composes `IMonthlyCostQuery` — the **first read side** of
the otherwise append-only cost ledger — whose SQL sums a half-open `[monthStart, monthStart+1 month)`
window with `FILTER (WHERE kind = …)` so estimate and actual come back per `(stage, tier)` in one pass and
drift is computed in the handler. A zero estimate against real spend is unbounded drift, **flagged as `∞`
rather than divided by**. Read-only.

**`/sources`.** Lists per-provider fetch health through `ISourceHealthQuery` and the day's degraded
coverage through `IDegradedCoverageQuery`, with a release button on each quarantined source (R4). The
button's tap runs `SourceQuarantineService.UnquarantineAsync` — the same aggregate path the F9 admin
endpoint uses, which also resets the consecutive-failure counter — and is **confirmed before it lifts the
hold**. The read is live here; the button-tap route is deferred to T10 with the rest of the callback rewire.

**`/run` — refused when live, previewed otherwise.** Triggers the daily pipeline off its 07:00 schedule.
It is **refused with an explanation when a Run is already live** (`IRunRepository.FindActiveRunAsync`), so
a rival Run is never started — the orchestrator refuses a second Run on the write side too; this just says
so before previewing or storing anything. Otherwise it reproduces the scope the orchestrator would take —
the live jobs first seen since the previous Run's cut-off (`IRunRepository.FindMostRecentCutoffAsync`),
or `now − RunOptions.InitialLookBack` when there is no previous Run — counts those first seen at or before
now through `ILiveJobsQuery.DiscoveredSinceAsync`, and names the honest cost cap: the snapshotted
`RunOptions.CeilingUsd` every Run is created under. No pre-submission cost *estimate* is shown because that
needs the rendered prompts the Worker builds (invariant 6 is enforced Worker-side, before submission), so
the ceiling is the true figure. It then stores a short-lived per-chat `ConversationState` and asks.

**`/redeliver` — states how many cards would actually be sent.** Re-delivers today's digest, safe by
construction: the `delivery_log` means an already-sent card is never sent again (invariant 8), so the whole
value of the command is honesty about that. It resolves the day's Run as delivery does (the live one, else
the most recent — including a terminal Run, the degraded day whose digest the Owner may still want re-sent),
renders the stored digest through `IDigestRenderer`, and set-differences the rendered card keys against
`IDeliveryLog.DeliveredKeysAsync(runId, chatId)` — the same idempotence key the delivery loop skips on — to
get the would-be-sent count, **usually zero**. With no Run or no assembled digest it says so plainly and
stores nothing. Otherwise it stores a pending `ConversationState` and asks.

**Deferred to T10.** As with T03–T08, this task ships the mechanism, not the live callback wiring. The
three state-changing commands preview and store a pending confirm state; the routing that resumes each on
the Owner's confirmation — `/run`'s `StartDailyRun` publish (the Telegram host does not run Wolverine, so
`IMessageBus` is not yet in its container), `/redeliver`'s republish, and `/sources`' release-button
`UnquarantineAsync` — is wired with the dispatch rewire against the full command registry (T10). Every read
and every preview here is live; only the confirm-tap routes are deferred.

## Links

[[../contracts/command-catalogue|catalogue]] §Operations · [[../../../operations/runbooks|R1]] · [[../../../operations/runbooks|R3]] · [[../../../operations/runbooks|R4]] · [[../../f3-claude-batch-enrichment/index|F3 Run]] · [[../../f5-daily-digest-delivery/index|F5 delivery]]
