# T01 — CommandDescriptor, registry and argument spec

**Layer:** domain/app · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

`CommandDescriptor`, `CommandScope`, `ArgumentSpec` and `CommandRegistry` per
[[../adr/0001-declarative-command-registry|ADR-F10-0001]]. The registry is the single place the
command surface is defined; the menu, help, authorization and doc conformance all derive from it.

## Done when

- A descriptor without a scope cannot be constructed — the guard is at construction, not at dispatch.
- A `ChangesState` descriptor without a confirmation path fails registry validation at startup.
- `ContractAnchor` is required on every descriptor.
- The whole command surface is readable in one file — that is the point of the design.
- Registry validation runs at startup and fails fast, so a malformed surface never serves traffic.

## Links

[[../adr/0001-declarative-command-registry|ADR-F10-0001]] · [[../sad]] §5
