---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f3-claude-batch-enrichment, jobhunter]
---

# F3-0002 — Estimate and ledger cost before submitting, never after

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

[[../../../CONTEXT]] invariant 6 says a Run never exceeds its cost ceiling. That sentence can be
implemented two ways, and the difference is the whole decision: a *budget alert* tells you after the
money is gone; a *precondition* prevents it. [[../../../DECISION-LOG|D4]] chose the latter as a
product decision; this ADR records how.

The failure this protects against is not a slow drift in monthly spend. It is a retry loop, a
misconfigured tier, or a discovery bug that puts 40 000 jobs in scope instead of 150 — any of which
can spend a month's budget before anyone looks at a dashboard.

## Decision drivers

- The Owner's tolerance for a surprise bill is essentially zero; anxiety about cost would kill the
  project faster than any technical problem.
- A control that runs after the fact is not a control.
- The check must be cheap enough to run before every submission, and accurate enough that the ceiling
  means something.
- It must be **testable**, which in practice means it must be possible to assert that no spend occurred.

## Considered options

1. **Monthly budget alert** at the provider level.
2. **Post-hoc ledger** — record actual cost after each batch, abort the *next* stage if over.
3. **Pre-submission estimate**, written to the ledger before the call, with the ceiling checked
   against estimates-plus-actuals.
4. **Hard rate limiting** — cap items per Run and trust the arithmetic.

## Decision outcome

**Chosen: Option 3.**

Before every submission:

1. `CostAccountant.Estimate(items, tier, promptVersion)` counts tokens from the **actually rendered
   prompt** — not a per-job heuristic — and prices them from the configured table, applying the batch
   discount. Output tokens are estimated pessimistically from the schema's maximum plausible size.
2. The estimate is written to `cost_ledger_entries` with `kind = 'Estimated'`, **committed before
   `SubmitAsync` is called** (AC-04). The ledger is therefore never behind reality, and a crash in
   between leaves an over-count rather than an under-count — the safe direction.
3. The ceiling check sums `Actual` for retrieved batches plus `Estimated` for outstanding ones, never
   both for the same batch. If `sum + estimate > ceiling_usd`, the client is **not called**: the Run
   becomes `CostAborted`, `RunCostAborted` is published, and a reduced digest still ships.
4. On retrieval, an `Actual` entry is written from the provider's reported usage. Both kinds are
   retained, which is what makes estimate accuracy measurable — a systematically drifting estimate
   means the pricing table is stale, and that is a tracked metric rather than a discovery.

`ceiling_usd` is **snapshotted onto the Run at creation**, so changing configuration mid-Run cannot
retroactively authorise spend that was already refused.

Option 1 is not a control. Option 2 lets a single runaway batch through, which is precisely the
scenario that matters. Option 4 breaks the moment prompt size changes, and it silently truncates,
which invariant 6 forbids.

## Consequences

**Positive**
- Overspend is prevented rather than reported. The invariant is a precondition with a test.
- The test is an **absence assertion**: a fake client whose `SubmitAsync` throws, and the test passes
  only if it is never invoked. Far stronger than asserting a resulting state.
- Estimate-versus-actual is a first-class metric, so pricing drift is visible before it matters.
- Cost is attributable per stage and per tier for free (AC-10).

**Negative**
- Rendering prompts to count tokens costs work before deciding to submit. At 150 items this is
  milliseconds, and it is why the estimate is accurate rather than approximate.
- A pessimistic output estimate means the ceiling binds slightly earlier than strictly necessary.
  Deliberate: erring toward under-spending is the correct asymmetry.
- The pricing table must be maintained. Made visible by the accuracy metric and a > 20% drift alert.

**Neutral**
- The same accountant serves F4, F5 and F8 with no changes — only the tier and stage differ.

## Links

- [[../../../CONTEXT]] invariant 6 · [[../../../DECISION-LOG|D4]] · [[../sad]] §10 QG-2
- [[../contracts/enrichment-schema]] §Cost model · [[../../../operations/runbooks|R3]]
