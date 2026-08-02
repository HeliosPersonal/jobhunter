---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0006 — Schema-bound structured output with tolerant parsing

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Four model outputs must become rows in a relational database: `Enrichment`, `Match`, `Digest` and
`CompanyResearch`. Free-text parsing is where projects of this shape rot — a model changes its
phrasing, a regex stops matching, and the pipeline either throws at 03:00 or silently writes
garbage. We need a contract that is machine-checkable and a failure mode that degrades one item
rather than one Run.

`wisewizard` deliberately used a line-delimited `KEY: value` format to be tolerant of malformed
output. That works but throws away type information and cannot express nested arrays like
`technologies[]` or `reasons[]` cleanly.

## Decision drivers

- Every field that reaches a column must be validated before it gets there.
- One malformed item out of 150 must not fail the Run (edge case in [[../idea-brief]] §9).
- Output parsing must be unit-testable with zero network, against saved fixtures.
- Prompt and schema must be versioned together — a schema change is a prompt change.

## Considered options

1. **Free-text prose plus regex extraction.**
2. **Line-delimited `KEY: value` blocks** (the `wisewizard` approach).
3. **JSON Schema enforced through a tool-use ("structured output") definition, parsed with `System.Text.Json` into typed records.**
4. **JSON requested in the prompt with no schema enforcement.**

## Decision outcome

**Chosen: Option 3.**

- Each output type has a versioned C# record, a JSON Schema generated from it, and a prompt builder
  in `JobHunter.Claude/Prompts/`. The schema is supplied to the model as a tool definition and the
  model is required to call it, so the provider constrains generation rather than us hoping.
- Parsing is **per item and tolerant**: each item in a batch result is parsed independently. A valid
  item is persisted; an invalid item is recorded as `EnrichmentFailed` / `MatchFailed` with the raw
  payload retained for inspection and retried once, at cheap tier, in the next Run.
- Semantic validation runs after schema validation: `score` clamped to 0–100, `technologies`
  de-duplicated against a known-tech vocabulary, `reasons` non-empty (invariant 4), salary parsed
  into `(min, max, currency)` or dropped rather than stored as prose.
- Prompts are C# raw string literals with an explicit `PromptVersion` constant. Changing a prompt
  bumps the version, which appears on every row it produced — so a quality regression is
  attributable.

## Consequences

**Positive**
- Type-safe outputs; schema violations are caught at the boundary, never in a `numeric` column.
- One bad item costs one item. The Run completes.
- Prompt regressions are testable: golden fixtures for 50 hand-labelled jobs run in CI with zero network.

**Negative**
- Schema-constrained generation slightly reduces expressive latitude; the `reasons[]` free-text
  field is deliberately kept unconstrained to compensate.
- Two artifacts to keep in sync per output type (record + prompt). Mitigated by generating the
  schema from the record rather than hand-writing it.

**Neutral**
- The same contract shape works against Ollama's structured-output mode, so the fallback path needs
  no separate parser.

## Links

- SAD: [[../sad]] §6.2, §8
- Related: [[0005-anthropic-message-batches-two-tier-cascade]]
- Feature: [[../../features/f3-claude-batch-enrichment/index|F3]]
