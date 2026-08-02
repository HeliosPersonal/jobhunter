# T03 — Dispatcher: allowlist, resolution, capability, rate limit

**Layer:** telegram · **Deps:** T02 · **Est:** M · **Owner:** Viacheslav

## What

`CommandDispatcher`: allowlist before anything else, resolve against the registry, check the
command's `CommandCapability`, apply the per-chat rate limit, invoke. The ordering matters — an
unauthorised chat must be dropped before resolution, so probing reveals nothing about the catalogue.

## Done when

- An unauthorised chat is dropped before resolution and receives **nothing**; only a log line is written (AC-10).
- The `CommandCapability` is read from the descriptor; a `Sensitive` command gates behind confirmation and there is no path to a handler that bypasses it.
- The 21st command in a minute is throttled with exactly **one** message per window, not one per command.
- Every invocation is recorded with command, outcome and duration — never argument content.
- Read-command p95 stays under 2 s against seeded data.

## Links

[[../sad]] §6.1 · [[../data-model]]
