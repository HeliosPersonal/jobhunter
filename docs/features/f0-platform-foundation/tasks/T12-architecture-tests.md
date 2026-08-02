# T12 — Architecture tests

**Layer:** tests · **Deps:** T07, T08 · **Est:** M · **Owner:** Viacheslav

## What

`JobHunter.ArchitectureTests` asserting every one of the **eight** rules in
[[../../../engineering/coding-standards|standards]] §2 — `Domain` referencing no package beyond
`Microsoft.Extensions.*.Abstractions`, dependency direction, `Contracts` referencing nothing, Dapper
never writing, no ambient clock, AppHost isolation, `internal sealed` entity configurations, no stray
`public` types in Infrastructure. Each test ships with a deliberately violating fixture in an excluded
`~Violations` folder.

## Done when

- All eight rules from [[../../../engineering/coding-standards|standards]] §2 are asserted.
- Introducing a violation fails the build naming the rule (AC-08).
- Each test has a matching violating fixture proving the assertion can go red.
- The suite runs in under 5 s so it never becomes the reason tests are skipped.

## Links

[[../../../engineering/coding-standards]] §2 · [[../sad]] §10 QG-2
