# T05 — Job closure handling

**Layer:** app · **Deps:** T03 · **Est:** S · **Owner:** Viacheslav

## What

Consume `JobClosed` from F2: mark the application's posting closed and record a system
transition note. **The status is deliberately not changed** — a posting closing tells us nothing about
the Owner's application, and auto-rejecting would fabricate an outcome that poisons F7's evidence.

## Done when

- A closed posting sets the flag and records a `System`-sourced note without changing the status (AC-07).
- The application and its full history are retained — nothing is deleted.
- A closure for a terminal or non-existent application is a no-op, not an error.
- A `Saved` application whose posting closed triggers a reminder suggesting drop or apply elsewhere.
- The handler is idempotent — a redelivered closure changes nothing further.

## Links

[[../sad]] §6.3 · [[../../f2-normalization-dedup/tasks/T08-lifecycle-grouping|F2 T08]]
