---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f10-telegram-commands, jobhunter]
---

# F10-0001 — A declarative command registry, not a switch statement

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Twenty commands, each needing a name, a description for the client menu, an argument spec, an
authorization scope, a flag for whether it changes state, a help entry, and documentation. The
obvious implementation is a `switch` on the command word plus a hand-maintained menu and a
hand-written help string.

That shape has a specific failure mode, and it is not hypothetical: the pieces drift. A command gets
renamed and the menu still shows the old name. A command is added and nobody updates `/help`. A
command that mutates state ships without a confirmation because the author of that one handler did
not know the convention. None of these fail a test, because there is nothing to test *against* —
each piece is independently plausible.

## Decision drivers

- The surface will grow. Twenty-two commands today, more when a feature lands; the mechanism has to
  make growth safe rather than rely on the author remembering four places to edit.
- Authorization must fail closed. A command reachable without a declared capability is the one bug in
  this feature that actually matters.
- [[../../../CONTEXT]] invariant 12 and the F5 boundary mean some commands are more dangerous than
  they look — `/run` spends money, `/research` triggers a paid deep-tier fetch.
- The catalogue is documentation the Owner reads. Documentation that has drifted is worse than none.

## Considered options

1. **Switch statement + hand-maintained menu, help and docs.**
2. **Attribute-decorated handler methods**, discovered by reflection.
3. **A declarative `CommandDescriptor` registry**, from which the menu, help, authorization and doc
   conformance all derive.
4. **Configuration file** (YAML) defining commands, with handlers resolved by name.

## Decision outcome

**Chosen: Option 3.**

Each command is one `CommandDescriptor` record: name, summary, argument spec, `CommandCapability`,
`ChangesState`, handler, and a `ContractAnchor` naming its heading in
[[../contracts/command-catalogue|the catalogue]]. Four things derive from it and nothing is
hand-maintained:

| Derived | How |
|---|---|
| Client menu | `BotMenuSynchroniser` generates `setMyCommands` at startup |
| `/help` output | Rendered from summaries, grouped by section |
| Authorization | The dispatcher reads `CommandCapability` before invoking; there is no other path to a handler |
| Confirmation | `ChangesState` alone decides whether a confirmation is required |

The `ContractAnchor` is what closes the loop on documentation. The conformance suite asserts a
**bijection** between descriptors and catalogue headings — a command built but not documented fails,
*and* a command documented but not built fails. The second direction is the one usually missing, and
it is how a catalogue quietly becomes fiction.

Option 2 is close and would work, but attributes scatter the surface across files: you cannot read
the whole command set in one place, which is exactly what a reviewer and the Owner both want. Option 4
adds a parse step and a name-resolution failure mode at startup, buying configurability nobody asked
for — the command set is not something to change without a deploy.

## Consequences

**Positive**
- The menu, help and docs cannot drift — they are outputs, not copies.
- A command cannot ship without a capability or without documentation; both fail the build.
- The whole surface is readable in one file, which is what makes review possible at 22 commands.
- Adding a command is a descriptor plus a handler plus a doc section — a checklist the compiler enforces.

**Negative**
- Slight indirection: finding a handler means going through the registry rather than to a `case`.
  Acceptable, and the registry doubles as the index.
- The registry is one file that every command touches, so it is a merge-conflict point. Irrelevant at
  one engineer, and it would be alphabetical-ordering conflicts at worst.

**Neutral**
- The same pattern already exists one layer over in [[../../f9-search-and-api/index|F9]]'s
  endpoint-convention test. Two surfaces, one idea: the thing that lists what exists is also the thing
  that enforces it.

## Links

- [[../sad]] §4 S1, §10 QG-1, QG-2 · [[../contracts/command-catalogue]] · [[../test-plan]] §The catalogue-conformance suite
- [[../../f9-search-and-api/test-plan|F9]] §The endpoint-convention test
