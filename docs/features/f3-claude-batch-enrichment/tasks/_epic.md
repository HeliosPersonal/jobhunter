---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f3-claude-batch-enrichment, mvp, jobhunter]
---

# Epic — F3 Claude Batch Enrichment

Introduce the **Run** — a durable, resumable, cost-bounded state machine owning one day's
intelligence work — and the **Batch** lifecycle that survives hours of asynchronous waiting. Use both
to produce an `Enrichment` for every newly discovered job.

The interesting engineering here is not the prompt. It is surviving the wait, and never paying twice
for work already done.

**F4, F5 and F8 reuse this machinery unchanged.** They add a `stage` value; they do not extend the
mechanism. Building it properly once is why F3 precedes F4.

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-07, AC-01…AC-12
- SAD: [[../sad|sad]] — Run state machine, batch lifecycle, cost enforcement
- Data model: [[../data-model|data-model]] — five owned tables, three of them shared downstream
- Contract: [[../contracts/enrichment-schema|enrichment schema]] — schema, prompt, parsing rules, cost model
- Test plan: [[../test-plan|test-plan]] — crash matrix, fixture corpus, golden set
- ADRs: [[../../../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|0005]],
  [[../../../00-overview/adr/0006-structured-output-contract|0006]],
  [[../adr/0001-run-as-resumable-state-machine|F3-0001]],
  [[../adr/0002-pre-submission-cost-ceiling|F3-0002]]

## Scope

**In:** the Run aggregate and orchestrator, the Batch lifecycle and poller, cost estimation and the
append-only ledger, `ILlmBatchClient` and the Anthropic adapter, the enrichment prompt and schema,
per-item tolerant parsing, the once-only retry policy.
**Out:** CV matching (F4), ranking (F4/F7), digest synthesis (F5), company research (F8) — all of
which submit through this machinery but own their own prompts and outputs.

**Hard boundary:** the CV never enters an F3 prompt. Enrichment describes the job, not the fit.

## Module scope

`Domain/Pipeline`, `Domain/Intelligence/Enrichment`, `Domain/Abstractions/ILlmBatchClient`,
`Application/Enrichment`, `JobHunter.Claude` (client, prompts, schemas, parsing, cost, fixtures),
`Infrastructure/Persistence` (five tables).

## Handoff interfaces

| Provides | Consumer |
|---|---|
| `Run` aggregate and `RunOrchestrator` | F4, F5, F8 |
| `ILlmBatchClient` + `AnthropicBatchClient` | F4, F5, F8 |
| `CostAccountant` + ledger | F4, F5, F8 |
| `BatchPollJob` + `TolerantJsonParser` | F4, F5, F8 |
| `EnrichmentCompleted` | F4 matching |
| `RunCostAborted` | F5 reporting, Telegram |
| `enrichments` table | F4, F5, F9 (read-only) |

## Tasks

See [[tracker|tracker]]. 14 tasks, ≈ 7.75 person-days.

## Definition of Done (epic)

- AC-01…AC-12 covered by passing tests.
- **All eight crash-matrix checkpoints pass**, each asserting exactly one submission.
- The ceiling test passes by asserting the client is **never called**, not by asserting a state.
- A mixed-validity batch stores 147 and records 3, and the Run completes.
- Enrichment cost < $0.50 (NFR); ≈$0.43 typical at 150 jobs; estimates within 20% of actuals.
- Parse success ≥ 97% on the golden set.
- Every enrichment carries at least one reason and a prompt version.
- Contributes to milestone M3 in [[../../../BACKLOG|BACKLOG]] §1.
