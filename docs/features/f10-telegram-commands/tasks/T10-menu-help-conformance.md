# T10 — Menu sync, help, suggestions and conformance suite

**Layer:** telegram/tests · **Deps:** T06, T07, T08, T09 · **Est:** L · **Owner:** Viacheslav

## What

`BotMenuSynchroniser`, grouped `/help`, `/start`, edit-distance suggestions — and the
conformance suite that keeps all of it honest. The suite is the feature's real deliverable: without
it, 22 commands drift apart within a month.

## Done when

- The client menu is generated from the registry at startup and matches it exactly (AC-01).
- **Registry → contract**: every descriptor's anchor resolves to a heading in the catalogue.
- **Contract → registry**: every command heading has a descriptor — a documented-but-unbuilt command fails the build.
- **Registry → safety**: every command declares a capability and every state-changing one has a confirmation path (AC-12).
- Each conformance assertion has a deliberately non-compliant fixture proving it can go red.
- Every single-character typo in the fixture set resolves to the right suggestion (AC-09).
- `/start` from an unauthorised chat returns nothing and never reveals the catalogue (AC-10).
- Every command has a committed rendering snapshot in the shared F5 corpus.

## Implementation

**The descriptor catalogue is the one source.** `CommandCatalogue.Descriptors` is the single canonical
registry, in catalogue order; the client menu, the grouped `/help`, `/start`, the unknown-command reply,
authorization and the conformance suite all project from it, so a routable command is always listed and a
listed command is always routable, and they cannot drift (ADR-F10-0001). The registry constructor fails
closed: an `Unspecified` capability throws, and a `changesState` command with no confirmation prompt throws
— a mis-declared command cannot reach the running bot.

**Menu sync (AC-01).** `BotMenu` projects the registry to the Telegram command list and
`BotMenuSynchroniser` pushes it with `setMyCommands` at startup, so the menu the Owner sees is generated
from the registry rather than maintained by hand.

**Grouped help and usage (AC-09).** `HelpText.Grouped` renders `/help` as sections by `CommandGroup`,
omitting empty groups; `HelpText.Usage` renders `/help [command]` for one descriptor; `/start` is a fixed
greeting followed by the same grouped list. `CommandSuggester` resolves an unknown token to the nearest
command by Damerau–Levenshtein distance (within two edits) and `UnknownCommandFormatter` shows the mistyped
token inside a code span so Telegram cannot linkify the typo, with the suggestion as the one tappable
command — never an LLM, never a conversational fallback.

**The conformance suite is the real deliverable.** `CommandCatalogueConformanceTests` asserts all three
directions — registry → contract (every descriptor's anchor resolves to a catalogue heading), contract →
registry (every heading has a descriptor, so a documented-but-unbuilt command fails the build), and
registry → safety (every command declares a capability and every state-changing one has a confirmation
path, AC-12) — and each assertion has a deliberately non-compliant fixture proving it can go red.

**The command-surface corpus.** `CommandSurfaceSnapshotTests` snapshots every command's `/help [command]`
usage plus the four whole-catalogue surfaces (grouped help, `/start` greeting, near-typo suggestion,
far-token fallback) into the shared F5 rendering corpus, under the bootstrap-once/never-overwrite
discipline. Because the per-command theory is driven from the live descriptor list, a command added without
a reviewed snapshot fails the build — the bytes the Owner reads are pinned to the registry.

**AC-10.** `OwnerGatedUpdateProcessor` drops any update whose chat is not the Owner before dispatch, so
`/start` from an unauthorised chat returns nothing and never reveals the catalogue.

**Remaining — the confirm-resume rewire (S5).** State-changing commands (`/run`, `/redeliver`, `/note`,
`/floor`, `/research`, `/forget`, and the `/sources` release button) preview and store a pending
`ConversationState`; the routing that resumes each on the Owner's `confirm` reply, the `/cancel` handler,
and the `IConversationStateStore` registration are the last slice of this task and are tracked separately
until they land.

## Links

[[../test-plan]] §The catalogue-conformance suite · [[../adr/0001-declarative-command-registry|ADR-F10-0001]]
