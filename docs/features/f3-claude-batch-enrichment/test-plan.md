---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f3-claude-batch-enrichment, mvp, jobhunter]
---

# Test plan — f3-claude-batch-enrichment

> Two suites carry this feature: the **crash matrix** (QG-1) and the **ceiling assertion** (QG-2).
> Both assert absence — no duplicate charge, no client call — which is stronger than asserting state.

## Levels

| Level | Scope | Network | Docker | Tooling |
|---|---|---|---|---|
| Unit | Run state transitions, cost arithmetic, token estimation, tolerant parsing, backoff schedule | No | No | xUnit |
| Fixture | Batch result parsing: valid, malformed, mixed, empty, unknown enums, oversized | No | No | xUnit + `Fixtures/` |
| Integration | Run persistence, batch uniqueness, ledger append-only, enrichment upsert | No | Yes | Testcontainers |
| **Crash matrix** | Kill at each of 8 checkpoints, assert convergence and single-submission | No | Yes | Testcontainers + fake client |
| Messaging | Stage chain, redelivery, deadline carry-over | No | Yes | Testcontainers |
| Golden | 50 hand-labelled jobs, expected field bands against recorded output | No | No | xUnit |
| Live drift | 10 items through the real provider, compared to fixtures | **Yes** | No | Nightly, alert-only |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `Run_OverJobsInScope_ProducesOneEnrichmentPerJob` | Messaging |
| AC-02 | `EnrichmentWithNoReasons_IsRejected_AndRecordedFailed` | Fixture |
| AC-03 | `EstimateExceedingCeiling_NeverCallsClient_AndAbortsRun` | Unit + Integration |
| AC-04 | `EstimatedCost_IsLedgered_BeforeSubmitIsCalled` | Integration |
| AC-05 | `RestartWithBatchInFlight_PollsExistingBatch_AndSubmitsExactlyOnce` | Crash matrix |
| AC-06 | `ReprocessingSameResults_ProducesNoDuplicateEnrichments_AndNoExtraCost` | Crash matrix |
| AC-07 | `MixedValidityBatch_StoresValid_RecordsInvalid_AndCompletesRun` | Fixture + Integration |
| AC-08 | `FailedItem_RetriesOnceNextRun_ThenIsAbandoned` | Integration |
| AC-09 | `BatchIncompleteAtDeadline_ShipsPartial_AndCarriesOver` | Messaging |
| AC-10 | `CostIsAttributable_PerStageAndTier_AndSumsToTotal` | Integration |
| AC-11 | `EveryEnrichment_RecordsItsPromptVersion` | Integration |
| AC-12 | `RunControl_WithoutOperatorScope_IsRefused` | Smoke |

## The crash matrix

The centrepiece. Eight checkpoints, each a separate test, each killing the worker mid-flight and
restarting it:

| # | Kill point | Must hold after restart |
|---|---|---|
| 1 | After Run created, before scope selected | Run resumes, scope computed once |
| 2 | After scope, before the estimate is ledgered | No submission has happened; estimate written once |
| 3 | After the ledger entry, before `SubmitAsync` | Submission happens exactly once; no orphan ledger entry double-counted |
| 4 | **After `SubmitAsync` returns, before the batch row commits** | Reconciliation finds the provider-side batch and adopts it — **no second submission** |
| 5 | After the batch row commits, before the first poll | Polling resumes from the persisted provider id |
| 6 | Mid-poll, status still in progress | Poll attempts continue; no resubmission |
| 7 | Mid-result-processing, some items stored | Reprocessing stores the remainder, no duplicates |
| 8 | After all items stored, before the Run state advances | State advances once; enrichments unchanged |

Every case asserts, via a counting fake client, that `SubmitAsync` was invoked **exactly once** and
that the ledger's `Actual` entries total the same as an uninterrupted run. Checkpoint 4 is the one
that matters most — it is the only window where money can be spent without a record, and D5 in
[[sad|SAD]] §11 exists because of it.

## Fixture corpus

`JobHunter.Claude/Fixtures/enrichment/`:

| Fixture | Asserts |
|---|---|
| `valid-150.jsonl` | Happy path at realistic scale |
| `mixed-147-valid-3-bad.jsonl` | QG-3 — one bad item costs one item |
| `truncated-json.jsonl` | Malformed JSON recorded, raw retained |
| `schema-violation.jsonl` | Wrong type, out-of-range confidence, missing required field |
| `empty-reasons.jsonl` | Invariant 4 enforced even if the provider ignores `minItems` |
| `unknown-enum.jsonl` | A new enum value degrades to `Unknown` and does **not** throw |
| `null-salary.jsonl` | Absent salary is legal, not a failure |
| `inverted-salary.jsonl` | `max < min` swapped, anomaly logged |
| `unknown-currency.jsonl` | Salary dropped, the rest of the assessment kept |
| `provider-error-item.jsonl` | Per-item provider error recorded, batch continues |
| `empty-batch.jsonl` | Zero items is a valid completion, not an error |

## Edge cases / error paths

- Zero new jobs → the Run completes without submitting anything; ledger stays at zero; a digest is
  still produced ([[../../00-overview/idea-brief|brief]] §9 — silence is indistinguishable from breakage).
- Ceiling already exhausted by a previous stage → `CostAborted` before the first submission.
- Ceiling exhausted *between* the estimate and the submit → the estimate is ledgered first, so the
  check is against a ledger that already includes it; the race cannot under-count.
- Provider returns `expired` for a batch → marked `Expired`; items retry next Run at cheap tier.
- Provider returns results for an item never submitted → ignored and logged as a provider anomaly.
- Provider returns fewer results than items submitted → missing items recorded `ProviderError` and
  retried; the count mismatch is logged, not silently accepted.
- Two Runs somehow created for one day → the partial unique index rejects the second.
- Deep-tier configured with the same model id as cheap tier → startup validation fails (a
  misconfiguration that would silently double cost).
- Pricing table missing a tier → startup fails; an unpriced tier makes the ceiling meaningless.
- Poll deadline (6 h) reached → batch marked `Failed`, items carried over, Run proceeds.

## Test data

- `FakeLlmBatchClient` replaying fixtures with a configurable delay, a call counter, and a mode
  that **throws if `SubmitAsync` is called** — used by every ceiling test (QG-2).
- 50 hand-labelled jobs in `Data/golden-jobs.yaml` with expected **bands** (e.g. salary within
  ±25%, `aiUsage` ∈ {Medium, High}) rather than exact values — a model is not deterministic and a
  test that pretends otherwise is a flaky test.
- `RunBuilder`, `BatchBuilder` in `JobHunter.TestKit`.
- `FakeClock` drives every backoff and deadline; no test waits on real time.

## NFR validation

- Cost < $0.50 for enrichment at 150 jobs (assert against `PricingTable`; ≈$0.43 typical) → computed from the pricing table over the golden set.
- Estimate within 20% → asserted across the fixture corpus; drift beyond that fails.
- Cheap-tier share ≥ 70% → ledger assertion over a full simulated Run.
- Poll schedule → asserted against `FakeClock`: 2, 4, 8, 15, 15… capped at 6 h total.
- Parse success ≥ 97% → asserted on the golden set.
- **Resume: 0 duplicate charges** → the crash matrix, all eight cases.
- Run wall clock ≤ 5 h → simulated with a fake client returning `ended` after a configured delay.

## CI

- **PR:** unit, fixture, integration, crash matrix, messaging, golden.
- **Nightly:** live drift — 10 real items compared to fixtures, alerting on divergence, never
  gating a build. A provider-side change is news, not a regression in our code.
- **Gate G10:** any change to the prompt, schema or parsing rules must ship updated golden fixtures
  in the same PR.

## Related

[[../../engineering/testing-strategy]] §5 · [[contracts/enrichment-schema]] · [[sad]] §10
