# T04 — Match prompt, schema and parser

**Layer:** claude · **Deps:** T01 · **Est:** L · **Owner:** Viacheslav

## What

`MatchPrompt` (versioned) and `MatchSchema`, parsed through F3's tolerant parser.
**This is the only file in the codebase that renders CV text into a string**, and it is built to make
that structurally true: it takes CV text by value and has no logger and no telemetry dependency, so it
cannot emit even by accident.

## Done when

- `MatchPrompt` has no `ILogger` and no `ActivitySource` dependency — asserted by an architecture test.
- CV text is a by-value parameter; it never appears on a context object, a log scope or a span tag.
- The rendered prompt is snapshot-tested so a change is visible in a diff.
- A missing enrichment omits the enrichment lines entirely rather than filling them with `Unknown` (AC-09).
- An unrecognised interview-probability value degrades to `Low` and is logged, never throws.
- An empty reasons array is rejected as a parse failure (AC-02).
- Truncation happens at a section boundary and is recorded on the batch item.

## Links

[[../contracts/match-schema|contract]] · [[../../f3-claude-batch-enrichment/tasks/T08-prompt-schema-parser|F3 T08]]
