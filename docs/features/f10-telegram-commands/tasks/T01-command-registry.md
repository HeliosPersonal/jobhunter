# T01 — CommandDescriptor, registry and argument spec

**Layer:** domain/app · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

`CommandDescriptor`, `CommandCapability`, `ArgumentSpec` and `CommandRegistry` per
[[../adr/0001-declarative-command-registry|ADR-F10-0001]]. The registry is the single place the
command surface is defined; the menu, help, authorization and doc conformance all derive from it.
`CommandCapability { Standard, Sensitive }` is a per-command sensitivity flag, not a role — the Owner
remains the sole principal (invariant 9).

## Done when

- A descriptor without a capability cannot be constructed — the guard is at construction, not at dispatch.
- A `ChangesState` descriptor without a confirmation path fails registry validation at startup.
- `ContractAnchor` is required on every descriptor.
- The whole command surface is readable in one file — that is the point of the design.
- Registry validation runs at startup and fails fast, so a malformed surface never serves traffic.

## Implementation

Two Domain records and one Application coordinator, with the two safety guards deliberately split across
the two layers so each fails at the moment it can first be wrong.

- **Domain (`JobHunter.Domain/Commands/`).** `CommandCapability { Unspecified, Standard, Sensitive }` — a
  per-command sensitivity flag, not a role (invariant 9); `Unspecified = 0` is the default enum value on
  purpose. `ArgumentSpec` is one positional argument (name, required, description), validated non-blank.
  `CommandDescriptor` carries name, summary, args, capability, `ChangesState`, `ContractAnchor` and an
  optional `ConfirmationPrompt`. Its constructor is the first guard: a descriptor is unconstructable
  without a name, a summary and a contract anchor, and unconstructable without a declared capability —
  `Unspecified` (a forgotten capability) and any undefined enum value are rejected, so authorization fails
  **closed** rather than silently defaulting to an everyday command (QG-2, Done-when a/c).
- **Application (`JobHunter.Application/Commands/CommandRegistry.cs`).** The single place the surface is
  assembled, self-validating at construction — and because it is constructed at startup, that is the
  fail-fast gate (Done-when d/e). It rejects an empty surface, a duplicate command name (a composition bug
  caught here, not a silent last-wins), and the load-bearing rule: a `ChangesState` descriptor with no
  `ConfirmationPrompt` throws, naming the offending command (Done-when b). This second guard lives at the
  registry rather than the descriptor because a confirmation path is a property of the assembled surface,
  and it keeps a descriptor constructable in isolation (e.g. in the conformance red-fixtures) while still
  making a malformed surface impossible to serve. `Find(name)` and `Commands` are what the menu, help,
  dispatcher and conformance suite all read — the whole surface readable in one list.

## Links

[[../adr/0001-declarative-command-registry|ADR-F10-0001]] · [[../sad]] §5
