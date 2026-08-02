---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f7-preference-learning, mvp, jobhunter]
---

# Test plan — f7-preference-learning

> The centrepiece is the **synthetic-behaviour corpus**: generate a fictional Owner with known
> preferences, generate signals consistent with them, and assert the fitter recovers what was planted.
> It is the only way to test a learner without waiting six months for real data.

## Levels

| Level | Scope | Docker | Tooling |
|---|---|---|---|
| Unit | Recency decay, bounding, normalisation, evidence floor, precedence rules | No | xUnit |
| **Synthetic corpus** | Generated Owners with known preferences; assert the weights recover them | No | xUnit + generators |
| Property | Adversarial signal distributions; bounds and the card floor always hold | No | xUnit |
| Integration | Model versioning, atomic activation, override persistence, rollback | Yes | Testcontainers |
| Pipeline | Ranking with and without a model; suppression reporting reaching the digest | Yes | Testcontainers |
| API | Explainability view, disable, reset — all owner-scoped | Yes | `WebApplicationFactory` |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `SufficientEvidence_ProducesAndActivatesNewModel` | Integration |
| AC-02 | `InsufficientEvidence_KeepsCurrentModel_AndRecordsReason` | Integration |
| AC-03 | `EveryWeight_IdentifiesItsSupportingSignals` | Unit + Integration |
| AC-04 | `SuppressedJob_IsCountedAndExplained_InDigest_AndRemainsRetrievable` | Pipeline |
| AC-05 | `ExplicitPreference_OverridesInferredOne_AndConflictIsRecorded` | Unit + Pipeline |
| AC-06 | `DisabledWeight_StopsAffectingOrdering_Immediately` | Integration |
| AC-07 | `LearningDisabled_AppliesOnlyExplicitPreferences_AndDigestSaysSo` | Pipeline |
| AC-08 | `PrecisionBeforeAndAfterActivation_IsComparableFromRecordedData` | Integration |
| AC-09 | `OneSidedEvidence_ProducesBoundedEffect` | **Property** |
| AC-10 | `PreferenceInspectionOrChange_WithoutOwnerScope_IsRefused` | API |

## The synthetic-behaviour corpus

`WeightFitter` is a pure function, so a whole fictional Owner can be simulated in milliseconds.

```
1. Define a fictional Owner with known preferences:
     salary floor 170k EUR, prefers DE/NL/remote-EMEA, dislikes Series-A,
     enthusiastic about Kafka and Rust, indifferent to company size otherwise.
2. Generate 400 signals consistent with those preferences, with realistic noise
   (10% of actions contradict the profile — people are not consistent).
3. Fit.
4. Assert the recovered weights point the right direction for each planted preference,
   and that no weight is invented for a dimension where the Owner was indifferent.
```

Profiles in the corpus, each testing a different failure mode:

| Profile | Asserts |
|---|---|
| **Clear preferences, low noise** | Baseline recovery; weights point the right way |
| **Clear preferences, 30% noise** | Weights are still directionally right but smaller — noise reduces confidence, not direction |
| **Indifferent** — actions uncorrelated with every dimension | **No weights are produced.** The most important negative case: a learner that invents preferences from noise is worse than none |
| **Correlated dimensions** — salary and company size move together | The combined effect stays bounded; the preference is not applied twice (SAD §11 D2) |
| **Changed mind** — first 200 signals prefer X, last 200 prefer Y | Recency decay makes Y dominate; the transition is visible in the weights |
| **Single dimension overwhelming** — 95% of ignores share one country | The weight is bounded at 0.40 of the component (AC-09) |
| **Almost everything ignored** | The digest still contains ≥ 3 cards (QG-3) |
| **Sparse** — 50 signals | No model is activated; the reason is recorded (AC-02) |
| **Outcome-heavy** — few taps, several interviews | Outcome signals dominate, per their higher weights |

The indifferent profile is the one that matters most. It is easy to build a learner that finds a
pattern in anything; the test that it finds *nothing* in noise is what separates learning from
superstition.

## Edge cases / error paths

- Exactly 199 and exactly 200 signals → the threshold boundary is asserted on both sides.
- All signals of one kind (all ignores, no saves) → weights are all negative; the card floor prevents
  an empty digest.
- A dimension value appearing in exactly 2 signals → **no weight produced** (evidence floor, AC-03).
- A job's facts missing a dimension entirely → that signal contributes to the other dimensions only.
- The active model is deleted mid-ranking → ranking falls back to zero preference component and
  renormalises; a test asserts the renormalisation, not just the absence of a crash.
- A weight disabled during a Run → takes effect on the next ranking, not mid-Run, so a single Run's
  ordering stays internally consistent.
- An explicit Profile preference contradicting a learned weight → explicit wins; the conflict is
  recorded and visible in the explainability view (AC-05).
- Refit while ranking is running → activation is atomic; ranking sees exactly one model, old or new.
- A `NeverSuppress` override on a value the model strongly suppresses → the job appears; the tension
  is recorded.
- 5 000 signals → refit under 30 s.

## Test data

- `SyntheticOwnerGenerator` producing the nine profiles above with a seeded RNG, so a failure is
  reproducible from its seed.
- `SignalBuilder` with realistic job-fact snapshots.
- `FakeClock` driving recency decay — the changed-mind profile spans a year in a millisecond.
- A recorded `precision@10` series before and after activation for AC-08.

## NFR validation

- 200-signal threshold → asserted at 199 and 200.
- Refit < 30 s for 5 000 signals → benchmark with a ceiling.
- Weekly schedule → schedule test with `FakeClock`, including a DST week.
- 180-day window with 60-day half-life → asserted by generating signals at known ages and checking
  their relative contribution.
- **No dimension above 0.40** → property test over adversarial distributions (AC-09).
- **100% of weights cite ≥ 3 signals** → asserted on every row, not sampled.
- 100% of suppressions reported → the digest breakdown must equal the suppressed row count, per reason.
- `precision@10` improvement → reported as a trend against the M4 baseline; not a CI assertion, and
  deliberately not claimed from a single week.

## CI

- **PR:** unit, synthetic corpus, property, integration, pipeline, API.
- **The synthetic corpus is the regression suite.** Any change to the fitting method must keep all
  nine profiles passing, including the indifferent one.

## Related

[[../../engineering/testing-strategy]] · [[sad]] §10 · [[adr/0001-transparent-frequency-weighting|ADR-F7-0001]]
