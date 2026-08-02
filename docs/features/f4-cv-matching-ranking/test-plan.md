---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f4-cv-matching-ranking, mvp, jobhunter]
---

# Test plan — f4-cv-matching-ranking

> Two suites define this feature: the **CV leakage scan** (QG-2, a security gate) and the
> **golden ranking set** (QG-1/QG-3, the quality gate).

## Levels

| Level | Scope | Network | Docker | Tooling |
|---|---|---|---|---|
| Unit | `ScoreCalculator`, freshness decay, suppression rules, CV text extraction | No | No | xUnit |
| Property | Ranking determinism under culture, ordering and clock variation | No | No | xUnit + FsCheck-style generators |
| Fixture | Match result parsing: valid, malformed, empty reasons, unknown band, missing salary | No | No | xUnit |
| **Leakage** | Sentinel-seeded CV through a full pipeline; scan every emitted artifact | No | Yes | Testcontainers + artifact scanner |
| Integration | CV versioning, activation, re-staling, score reconciliation | No | Yes | Testcontainers |
| Messaging | Matching stage chain, ranking, suppression reporting | No | Yes | Testcontainers |
| **Golden ranking** | 50 labelled jobs, expected top-5 ordering and score bands | No | No | xUnit |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `Matching_ProducesOneMatchPerJobPerRun_WithAllFields` | Messaging |
| AC-02 | `MatchWithNoReasons_IsRejected_AndRecordedFailed` | Fixture |
| AC-03 | `EveryScore_ReconcilesFromItsStoredComponents` | Integration |
| AC-04 | `ActivePreferences_InfluenceOrdering_AndTheirEffectIsRecorded` | Integration |
| AC-05 | `SuppressedJob_RecordsReason_AndRemainsRetrievable` | Integration |
| AC-06 | `CvSentinels_AppearInNoLogSpanIndexOrNotification` | **Leakage** |
| AC-07 | `CvUploadOrRead_WithoutOwnerScope_IsRefused` | Smoke |
| AC-08 | `ActivatingNewCv_StalesOldMatches_AndQueuesReMatch` | Integration |
| AC-09 | `JobWithoutEnrichment_IsStillMatched_WithReducedConfidence` | Messaging |
| AC-10 | `MalformedMatchResult_AffectsOnlyThatJob` | Fixture + Integration |
| AC-11 | `EveryNonSuppressedJob_HasExactlyOneScore_AndItIsDeterministic` | Property + Integration |

## The leakage suite

The feature's security gate, and the reason a security review is required before it ships.

```
1. Construct a CV containing 12 unique sentinel tokens (e.g. ZQX-7F31-KAFKA-SENTINEL),
   distributed across the summary, an employer name, a skill and a project description.
2. Run a complete pipeline against 20 fixture jobs: enrichment, matching, ranking,
   digest generation and delivery to a fake Telegram transport, with Typesense indexing enabled.
3. Collect every emitted artifact:
     - all log output at every level, including exception messages and stack traces
     - all OpenTelemetry span attributes and events
     - every Typesense document written
     - every Telegram message body and callback payload
     - every API response body from every endpoint except CV retrieval
     - the contents of batch_items.raw_result
4. Assert: zero occurrences of any sentinel, anywhere.
```

Additional adversarial cases in the same suite:

- **Forced failure paths.** Deliberately throw inside the matching handler with a sentinel-laden CV
  loaded, and assert the exception message and stack trace carry no sentinel. Failure paths are where
  leaks actually happen.
- **Debug logging enabled.** Re-run at `Debug` level — a leak that only appears when someone is
  investigating a problem is the worst kind.
- **Serialization.** Assert `MatchPrompt` and its inputs do not appear in any serialised message on
  the bus.

A single hit fails the build. There is no allowlist.

## The golden ranking set

`Data/golden-ranking.yaml` — 50 jobs against one fixed CV, each labelled with:

- an expected score **band** (`excellent` ≥ 75, `good` 55–75, `marginal` 40–55, `reject` < 40)
- for the top 5, the expected **relative ordering**

Asserting bands and relative order rather than exact scores is deliberate: the model is not
deterministic, and a test pretending otherwise is a flaky test that gets disabled within a month.

The set includes the cases that are easy to get wrong:

| Case | Expected |
|---|---|
| Perfect stack match, wrong seniority (junior role) | `reject` |
| Perfect stack match, stretch seniority (one level up) | `good` — a stretch is worth an application |
| Adjacent stack (Java rather than .NET), same domain | `marginal` |
| Ideal role, wrong timezone, not remote | suppressed with a timezone reason |
| Ideal role, salary well below floor | `good` but down-weighted, not suppressed by default |
| Contract role, Owner open to contract | no penalty |
| Contract role, Owner not open to contract | suppressed with an employment-type reason |
| Excellent fit, posted 20 days ago | still in the top 10 — freshness must not bury fit |
| Vague posting with almost no detail | `marginal`, with a reason naming the vagueness |
| No enrichment available | matched, confidence multiplier 0.85 applied |

**Gate:** a change to the match prompt, the schema or the ranking weights must keep the golden set
passing, or update it in the same PR with a stated reason.

## Edge cases / error paths

- No active CV → matching is skipped for the Run with a recorded reason; enrichment still ran, so the
  digest can still be produced from enrichment alone, degraded.
- CV re-uploaded with identical content → content hash matches, no new version, no re-staling.
- CV upload of 6 MB → refused at the cap, before extraction.
- PDF with no extractable text (a scan) → refused with a clear message; the system does not OCR.
- Zero enriched jobs → ranking produces nothing and the Run proceeds; no division by zero in any
  aggregate.
- All jobs suppressed → the digest reports "0 shown, N suppressed" with the breakdown. Silence would
  be indistinguishable from breakage.
- Preference model absent (before F7) → preference component is 0, weights renormalise across the
  remaining two, and a test asserts the renormalisation.
- Two jobs with identical scores → ordering is stable and deterministic, broken by job id.
- `matchScore` returned as a string → schema rejects; item recorded failed.
- Interview probability returned as an unrecognised value → degrades to `Low` and is logged, never throws.

## Test data

- One fixed reference CV in `Data/reference-cv.md` (fictional, sentinel-free) for the golden set.
- One sentinel-laden CV in `Data/sentinel-cv.md` for the leakage suite only.
- `FakeLlmBatchClient` (from F3) replaying `Fixtures/match/*.jsonl`.
- `PreferenceModelBuilder` producing deterministic weight sets.
- `FakeClock` for every freshness computation — no test depends on the real date.

## NFR validation

- **precision@10** → not CI-assertable. Measured weekly from Owner ratings and tracked; the golden
  set is the CI proxy.
- Matching cost < $0.35 → computed from the pricing table over 150 golden-set-sized items.
- **Determinism** → property test, 10 000 generated inputs, three cultures, shuffled input order.
- Ranking < 5 s for 500 jobs → benchmark, asserted as a ceiling.
- Parse success ≥ 97% → golden set.
- **CV leakage zero** → the leakage suite; any hit fails the build.
- Re-match window → integration test asserting exactly the last 30 days of live jobs are queued.
- Score explainability 100% → asserted on every persisted row, not sampled.

## CI

- **PR:** all levels including leakage and golden ranking.
- **Pre-ship security review:** required for this feature ([[PRD]] §6.1), with the leakage suite as
  its evidence.
- **Gate G10:** prompt, schema or weight changes ship an updated golden set in the same PR.

## Related

[[../../engineering/testing-strategy]] · [[contracts/match-schema]] §CV handling rules ·
[[../../engineering/security]] §1 · [[sad]] §10
