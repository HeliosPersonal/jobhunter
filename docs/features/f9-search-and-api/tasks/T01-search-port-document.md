# T01 — ISearchIndex port and JobDocument allowlist

**Layer:** domain/search · **Deps:** — · **Est:** S · **Owner:** Viacheslav

## What

The `ISearchIndex` and `ISearchQuery` ports, and `JobDocument` — a hand-written record
listing every indexed field explicitly. It is deliberately **not** a mapping from the `Job` aggregate,
because that is what makes accidental exposure structurally difficult rather than merely forbidden.

## Done when

- `JobDocument` lists every field explicitly; there is no reflection-based or convention-based mapping.
- A test asserts the document's field set exactly equals the Typesense schema's.
- No field carries match reasons, missing skills or application notes ([[../data-model|data-model]] §What is deliberately absent).
- The ports are provider-agnostic — no Typesense type appears in `JobHunter.Domain`.
- Projection is a pure function of its inputs, unit-testable with no infrastructure.

## Links

[[../data-model]] · [[../adr/0001-index-as-rebuildable-projection|ADR-F9-0001]]
