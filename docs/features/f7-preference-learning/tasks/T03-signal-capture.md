# T03 — Signal capture verification

**Layer:** app · **Deps:** T02 · **Est:** S · **Owner:** Viacheslav

## What

Verify end to end that F5 and F6 are writing signals correctly, and backfill any gap.
The critical property is that `job_facts` is snapshotted **at the moment of the action** — joining to
`jobs` at fitting time would let a later edit rewrite what the Owner is recorded as having reacted to.

## Done when

- Every card action from F5 produces exactly one signal with a complete facts snapshot.
- Every terminal outcome from F6 produces one weighted signal.
- Editing a job after a signal was captured does not change that signal's facts — asserted directly.
- Redelivered actions produce no duplicate signals.
- A backfill command exists for signals captured before this task, and is idempotent.

## Implementation

- **Facts are snapshotted at the action, not joined at fit time.** The card-action path (F5 T10) and the
  outcome path (F6 T08) each write `job_facts` into the signal when the action happens; the edit-after-capture
  integration (`SignalFactsImmutabilityTests`) asserts a later job edit never rewrites a captured signal's facts.
- **Backfill replays outcomes, not card taps.** The only durable trace predating the signals path is
  `application_transitions` (F6's append-only outcome history) — a card tap has no store of its own, so it
  cannot be replayed. The `backfill-signals` Worker verb replays terminal transitions
  (`Applied`/`Interview`/`Offer`/`Rejected`) that never minted a signal, snapshotting each job's *current*
  facts (the history holds none). Scope with `--since <yyyy-MM-dd>`; absent, it replays the full history.
- **Idempotence is structural, twice over.** `BackfillableOutcomeQuery` anti-joins out any outcome that
  already has a matching `(job_id, kind, occurred_at)` signal, and `ISignalRepository.TryCaptureAsync`
  re-checks the same unique key at insert (`ON CONFLICT DO NOTHING`). A second run — or two racing — captures
  nothing more. A job that has since closed has no live facts to snapshot, so its outcome is counted and
  skipped rather than turned into a factless signal; the printed report tallies examined / captured /
  already-present / without-facts.

## Links

[[../../f5-daily-digest-telegram/tasks/T10-callback-actions|F5 T10]] · [[../../f6-application-tracking/tasks/T08-outcome-signals|F6 T08]]
