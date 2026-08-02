---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f10-telegram-commands, mvp, jobhunter]
---

# Test plan — f10-telegram-commands

> The centrepiece is the **catalogue-conformance suite**: registry, client menu, help output and
> [[contracts/command-catalogue|the contract document]] must agree, in both directions. It is what
> keeps 22 commands from drifting apart.

## Levels

| Level | Scope | Docker | Tooling |
|---|---|---|---|
| Unit | Argument parsing, edit-distance suggestion, nonce lifecycle, rate-limit window | No | xUnit |
| **Conformance** | Registry ↔ menu ↔ help ↔ contract document bijection; capability declared on every command | No | Reflection over the registry + markdown parse |
| Rendering | Every command's output, via F5's fake notifier | No | Snapshot, shared corpus |
| Dispatch | Allowlist, resolution, multi-step resume, cancellation, throttling | Yes | Testcontainers (Redis) |
| Integration | Commands against real query services and real data | Yes | Testcontainers |
| Confirmation | Nonce issue, validate, burn, expire, replay | Yes | Testcontainers + `FakeClock` |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `GeneratedMenu_ContainsEveryRegisteredCommand_AndNothingElse` | **Conformance** |
| AC-02 | `Search_ReturnsCardsWithCountAndPagination` | Integration + Rendering |
| AC-03 | `Pipeline_GroupsByStatus_WithTransitionButtons` | Integration |
| AC-04 | `Hidden_ListsSuppressedWithReasonAndEvidence` | Integration |
| AC-05 | `Forget_DisablesWeight_AndStatesWhenItTakesEffect` | Integration |
| AC-06 | `Status_ReportsOutcomeCostCountsAndDegradedSources` | Integration |
| AC-07 | `StateChangingCommand_RequiresConfirmation_NamingTheEffect` | Confirmation |
| AC-08 | `PendingCommand_IsCancelledByCommand_AndByTimeout` | Dispatch |
| AC-09 | `UnknownCommand_SuggestsNearest_OrListsGroups` | Unit + Dispatch |
| AC-10 | `CommandFromUnauthorisedChat_IsDropped_AndRevealsNothing` | Dispatch |
| AC-11 | `UnknownCompany_OffersToAddIt_RatherThanEmptyResult` | Integration |
| AC-12 | `EveryCommand_DeclaresCapabilityAndStateChange` | **Conformance** |

## The catalogue-conformance suite

Four assertions, and the bidirectional ones are the load-bearing pair:

1. **Registry → contract.** Every `CommandDescriptor.ContractAnchor` resolves to a heading in
   `contracts/command-catalogue.md`. A command built but not documented fails.
2. **Contract → registry.** Every command heading in that document has a descriptor. A command
   documented but not built fails — this is the direction usually missed, and it is how a catalogue
   turns into fiction.
3. **Registry → menu.** The generated `setMyCommands` payload contains exactly the registered
   commands, with their summaries, and nothing else.
4. **Registry → safety.** Every descriptor declares a capability; every `ChangesState` descriptor has
   a confirmation path. A fixture violating each proves both can go red.

Adding a command therefore requires the descriptor *and* the documentation section, or the build
stays red — the mechanism that keeps [[sad|SAD]] §10 QG-1 true rather than aspirational.

## Edge cases / error paths

- `/search` with no query → asks for terms rather than returning the whole corpus.
- `/search` with only filters and no text → valid; filters alone are a query.
- `/search min:abc` → names the bad value and shows the usage line.
- `/search tech:kafka tech:kafka` → deduplicated, not double-weighted.
- `/more` with nothing below the cut → says so and reports the count shown.
- `/more 999` → clamped to 20 with a note.
- `/note` with no recent application → offers the last five to pick from.
- `/note` 5 000 characters → refused at the cap, with the note preserved in the reply so it is not lost.
- `/company` for a name matching two companies → both offered, disambiguated by domain.
- `/company` for a known company with no dossier → offers `/research` instead of an empty reply.
- `/forget` with no argument → lists disable-able weights.
- `/forget` for an already-disabled weight → says so; not an error.
- `/floor` with an unrecognised currency → names supported ones.
- `/run` while a Run is live → refused with the live Run's state and age.
- `/redeliver` when everything was already delivered → says "0 cards would be sent", the expected answer.
- `/cancel` with nothing pending → cheerful no-op.
- A second command while one is pending → the new command wins; the pending one is cancelled and that is stated.
- A confirmation tapped twice → the second says the confirmation was already used.
- A confirmation tapped after 2 minutes → expired, re-issue.
- 25 commands in a minute → throttled after 20, with **one** message, not five.
- A command during a Redis outage → multi-step commands degrade to requiring the argument inline; read commands are unaffected.
- Unauthorised chat sending `/start` → nothing back at all; only a log line (AC-10).

## Test data

- `CommandRegistryBuilder` for constructing partial registries, including deliberately non-compliant
  ones for the conformance red-tests.
- The F5 fake notifier and rendering corpus, reused — F10 adds cases, not a second harness.
- `FakeClock` for every TTL, timeout and nonce-expiry test; no test waits on real time.
- A fixture corpus of misspellings (`/pipline`, `/serach`, `/cmpany`, `/statuss`) with expected suggestions.

## NFR validation

- Read-command p95 < 2 s → benchmark per command against seeded data.
- Search p95 < 3 s including render → benchmark.
- **Menu accuracy 100%** → conformance assertions 1–3.
- Conversation timeout 5 min → asserted at 4:59 and 5:01 with `FakeClock`.
- Suggestion accuracy → every single-character typo in the fixture set resolves to the right command.
- **Authorization coverage 100%** → conformance assertion 4.
- Rate limit → 21st command in a window is throttled; exactly one throttle message per window.

## CI

- **PR:** all levels. Conformance runs first — it is the fastest and the most likely to catch a
  half-finished command.
- **On registry change:** the diff must show both the descriptor and the contract section, or
  conformance fails.

## Related

[[../../engineering/testing-strategy]] · [[contracts/command-catalogue]] · [[sad]] §10
