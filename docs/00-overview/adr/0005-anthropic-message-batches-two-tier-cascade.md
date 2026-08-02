---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0005 — Anthropic Message Batches API with a two-tier model cascade

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Two stages need a language model at volume: Enrichment (every new Job, ~150/day) and Matching
(every enriched Job against the CV). Two more need it at low volume: Digest synthesis (1 call) and
Company Research (~5/day). The daily budget is a couple of dollars at most, and QG-3 requires the
cost to be predictable and bounded, not merely observable after the fact.

## Decision drivers

- Cost: the Message Batches API is ~50% cheaper than synchronous calls.
- Volume asymmetry: 150 Enrichments vs 1 Digest — they should not use the same model.
- Latency tolerance: the Run starts at 02:00 and must deliver at 07:00. A five-hour window is ample
  for batch turnaround and makes synchronous calls pointless.
- The Batch lifecycle is asynchronous by nature, which forces the durable Run state that QG-2 wants
  anyway. The constraint and the goal point the same direction.

## Considered options

1. **Synchronous Messages API for everything.**
2. **Message Batches API, one tier (deep model for all stages).**
3. **Message Batches API, two-tier cascade: `Cheap` for high-volume triage/extraction, `Deep` for judgement.**
4. **Local Ollama on helios for everything.**

## Decision outcome

**Chosen: Option 3.**

| Stage | Tier | Volume/day | $/Run | Rationale |
|---|---|---|---|---|
| Enrichment | `Cheap` | ~150 | $0.43 | Structured extraction from a JD — a small model does this reliably against a schema |
| Matching | `Deep` | ~150 | $1.58 | Nuanced fit reasoning against a full CV; this is where quality is the product |
| Digest synthesis | `Deep` | 1 | $0.01 | One call, high visibility, worth the best model |
| Company research | `Deep` | ~5 | $0.14 | Synthesis over fetched evidence, must not hallucinate |

Tier → model, in configuration (verified against list pricing 2026-08-02):
`Cheap` = `claude-haiku-4-5` ($1.00/$5.00 per MTok), `Deep` = `claude-sonnet-5` ($3.00/$15.00).
Raising `Deep` to `claude-opus-5` ($5.00/$25.00) is a config change and roughly doubles the matching
line.

`ModelTier` is a domain concept; the tier→model-id and tier→price mapping lives in configuration, so
a model upgrade is a config change. `CostAccountant` estimates cost **before** submission and the
orchestrator refuses to submit if the estimate would breach the Run ceiling (invariant 6).

Local Ollama is retained as (a) the cheap-tier fallback when the Anthropic budget is exhausted and
(b) an offline development path — never as the primary judgement path, because an 8–14 B model
materially underperforms on the reasoning that *is* the product ([[../idea-brief|brief]] §7 D).

## Consequences

**Positive**
- ~50% discount on all model spend; the cascade cuts deep-tier volume by putting extraction on the cheap tier.
- A Run costs ≈ $2.16 at 150 jobs/day as designed here, and ≈ $1.03 once F4 adds the pre-match filter
  and CV prompt caching ([[../../ARCHITECTURE-OPEN-DECISIONS|O5]]) — roughly $31/month.
- The asynchronous lifecycle forces durable, resumable Run state — the property QG-2 demands.

**Negative**
- Up to 24 h Batch SLA. Mitigated by the partial-digest policy: 07:00 is never delayed; incomplete
  items roll into tomorrow.
- Two prompt/response contracts to design, version and regression-test rather than one.

**Neutral**
- An intermediate tier can be added later without changing the cascade shape, because
  `ILlmBatchClient` is provider- and tier-agnostic.

## Links

- Brief: [[../idea-brief]] §4, §7 Approach C
- SAD: [[../sad]] §4 S4, §6.2, §10 QG-3
- Related: [[0006-structured-output-contract]], [[0001-modular-monolith-three-deployables]]
