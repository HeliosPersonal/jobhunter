# T10 — Menu sync, help, suggestions and conformance suite

**Layer:** telegram/tests · **Deps:** T06, T07, T08, T09 · **Est:** L · **Owner:** Viacheslav

## What

`BotMenuSynchroniser`, grouped `/help`, `/start`, edit-distance suggestions — and the
conformance suite that keeps all of it honest. The suite is the feature's real deliverable: without
it, twenty commands drift apart within a month.

## Done when

- The client menu is generated from the registry at startup and matches it exactly (AC-01).
- **Registry → contract**: every descriptor's anchor resolves to a heading in the catalogue.
- **Contract → registry**: every command heading has a descriptor — a documented-but-unbuilt command fails the build.
- **Registry → safety**: every command declares a scope and every state-changing one has a confirmation path (AC-12).
- Each conformance assertion has a deliberately non-compliant fixture proving it can go red.
- Every single-character typo in the fixture set resolves to the right suggestion (AC-09).
- `/start` from an unauthorised chat returns nothing and never reveals the catalogue (AC-10).
- Every command has a committed rendering snapshot in the shared F5 corpus.

## Links

[[../test-plan]] §The catalogue-conformance suite · [[../adr/0001-declarative-command-registry|ADR-F10-0001]]
