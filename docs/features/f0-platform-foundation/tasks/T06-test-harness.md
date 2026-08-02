# T06 — Testcontainers harness (TestDatabase)

**Layer:** tests · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

`TestDatabase` in `JobHunter.Infrastructure.Tests`: one `postgres:17-alpine` container per
test run behind a semaphore-gated lazy singleton, one uniquely-named database per test, migrations
applied on create, `DROP DATABASE ... WITH (FORCE)` on dispose. Exactly the shape quoted in
[[../../../engineering/testing-strategy|testing strategy]] §3.

## Done when

- Two tests running concurrently never observe each other's data.
- Every test that uses the harness implicitly proves gate G3 (migrations apply on a clean DB).
- The container starts once per test run, not once per test — asserted by a start counter.
- A test that leaks a connection still allows the database to drop (`WITH (FORCE)`).
- Documented in [[../../../engineering/local-development|local development]] §7 including the Colima socket workaround.

## Out of scope

- RabbitMQ container — T08.

## Links

[[../../../engineering/testing-strategy]] §3 · [[../test-plan]]
