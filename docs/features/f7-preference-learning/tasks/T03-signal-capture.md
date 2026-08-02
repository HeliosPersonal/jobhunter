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

## Links

[[../../f5-daily-digest-telegram/tasks/T10-callback-actions|F5 T10]] · [[../../f6-application-tracking/tasks/T08-outcome-signals|F6 T08]]
