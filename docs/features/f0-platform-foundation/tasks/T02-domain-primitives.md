# T02 — Domain primitives: IClock, IIdGenerator, Result

**Layer:** domain · **Deps:** T01 · **Est:** S · **Owner:** Viacheslav

## What

The three primitives every later feature depends on. `IClock` with `SystemClock`
(`DateTimeOffset.UtcNow`) and `FakeClock` in the TestKit. `IIdGenerator` with
`UuidV7Generator` (`Guid.CreateVersion7()`) and a deterministic `SequentialIdGenerator` for tests.
`Result<T>` plus the outcome-enum convention from [[../../../engineering/coding-standards|standards]] §4.
Also the `ValueObject` and `Entity` base types.

## Done when

- `IClock` and `IIdGenerator` are the only sources of time and identity in the solution.
- `UuidV7Generator` produces monotonically increasing ids within a process (asserted over 10 000 draws).
- `Result<T>` supports map/bind and cannot represent success-with-error or failure-without-reason.
- `JobHunter.TestKit` exists and exposes `FakeClock` and `SequentialIdGenerator`.
- `JobHunter.Domain` references nothing outside `Microsoft.Extensions.*.Abstractions`.

## Out of scope

- Any domain entity — F0 owns no domain.

## Links

[[../sad]] §8 · [[../../../00-overview/adr/0015-uuidv7-keys-and-timestamptz|ADR-0015]]
