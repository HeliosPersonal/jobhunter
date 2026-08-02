# T03 — ILlmBatchClient port and fake implementation

**Layer:** domain · **Deps:** — · **Est:** S · **Owner:** Viacheslav

## What

The three-method port from [[../sad|SAD]] §5 and its DTOs, plus `FakeLlmBatchClient` in
the TestKit: fixture replay, configurable delay, a call counter, and a **throw-on-submit** mode. That
last mode is what makes the cost-ceiling test an absence assertion rather than a state assertion.

## Done when

- The port exposes exactly submit, status and streamed results — no provider concepts leak into it.
- `FakeLlmBatchClient` replays a JSONL fixture and counts every call by method.
- Throw-on-submit mode is available and used by the ceiling tests (QG-2).
- The fake can simulate `in_progress` for N polls, then `ended`, driven by `FakeClock`.
- `JobHunter.Domain` still references nothing external after this task.

## Links

[[../sad]] §5 · [[../test-plan]] §Test data
