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

## Implementation

Three parts, each built test-first, splitting the pure decision from the orchestration around it and
from the append-only audit it writes.

- **The rate limit — `CommandRateLimiter(IClock)` (Application).** A fixed 60-second window per chat:
  the first `Budget` (20, SAD §8) attempts are `Allowed`, the `(Budget + 1)`-th is `Throttled` — the
  one throttle message — and every further attempt that window is `Silenced`, so the Owner is not
  answered with one throttle reply per command (done-when #3). Clock-driven, so no test waits on real
  time.
- **The audit — `CommandInvocation` + `CommandOutcome` + `ICommandInvocationLog` (Domain), and
  `CommandInvocationLog` over `command_invocations` (Infrastructure).** The invocation type is the
  guarantee: it carries the command name, the outcome, the duration and the argument *count*, and has
  no field that could hold argument content, so "never argument content" (done-when #4) is a property
  of the type rather than a discipline at the call site. The write is a plain append (no `ON CONFLICT`
  — every attempt is a genuine new row); the outcome persists as text, never an ordinal.
- **The decision — pure `CommandDispatchPlanner` (Application).** Given a command word and its raw
  argument tail it resolves against the `CommandRegistry`, parses the arguments against the command's
  own inline-filter vocabulary, and returns a `CommandDispatchPlan` whose `DispatchAction` is
  `Proceed` / `Unknown` / `NeedsInput` / `Malformed` / `NeedsConfirmation`. It holds no chat, no clock
  and no I/O, so the two load-bearing rules are unit-testable in isolation: an unknown word is never
  parsed, and a `ChangesState` command never returns `Proceed` — there is no plan that reaches a
  handler without confirmation (done-when #2).
- **The orchestration — `DispatchCoordinator` (Telegram).** Composes the three: the allowlist has
  already dropped a non-Owner chat before this point (`OwnerGatedUpdateProcessor`, done-when #1), so
  the coordinator applies the rate limit, plans, then acts. An unknown, malformed or throttled outcome
  is a *terminal* dispatch and is audited with the command, outcome, duration and argument count; a
  command that only asks for a missing argument (T04) or issues a confirmation prompt (T05) has not
  run, so it is not audited here — the step that completes it owns that audit. A failed audit write is
  logged and swallowed: an operational fault, not a failed command.

**Deferred to [[T10-menu-help-conformance|T10]].** The live rewire of the dispatch path — switching
`OwnerGatedUpdateProcessor` to route through the `DispatchCoordinator` against the full 22-command
registry — lands with T10, where the registry is assembled as the single source of truth (QG-1) and
the conversation-state (T04) and confirmation-nonce (T05) steps complete the SAD §6.1 chain. Wiring a
live path through a partial registry before then would be throwaway. The nearest-suggestion reply for
an unknown command (AC-09) is also T10.

## Links

[[../sad]] §6.1 · [[../data-model]]
