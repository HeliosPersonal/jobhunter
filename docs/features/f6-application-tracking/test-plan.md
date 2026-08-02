---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f6-application-tracking, mvp, jobhunter]
---

# Test plan — f6-application-tracking

> The centrepiece is the **transition matrix suite** — all 36 status pairs enumerated, not the ones
> someone remembered.

## Levels

| Level | Scope | Docker | Tooling |
|---|---|---|---|
| Unit | `TransitionRules`, reminder thresholds, signal weighting | No | xUnit |
| **Matrix** | All 36 `(from, to)` pairs against the contract table | No | xUnit theory over the enum product |
| Integration | Application creation and advancement, history completeness, archival, closure | Yes | Testcontainers |
| Messaging | Digest action → application; `JobClosed` → marked, not rejected | Yes | Testcontainers |
| Sweep | Reminder suppression across simulated days | Yes | Testcontainers + `FakeClock` |
| API | Endpoint scopes, the 409 body, the pipeline projection | Yes | `WebApplicationFactory` |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `Pipeline_GroupsByStatus_MostRecentlyActiveFirst` | API + Integration |
| AC-02 | `Transition_AppliedWhenPermitted_RefusedOtherwise_HistoryNeverAltered` | **Matrix** |
| AC-03 | `EveryStageChange_AppearsInHistory_WithTimeAndSource` | Integration |
| AC-04 | `DigestAction_CreatesOrAdvancesApplication_AndAppearsInHistory` | Messaging |
| AC-05 | `StaleApplication_IsRemindedOnce_NotAgainUntilConditionRecurs` | **Sweep** |
| AC-06 | `Note_IsStoredWithTime_AndAppearsInHistory` | Integration |
| AC-07 | `JobClosure_MarksApplication_WithoutChangingStatus_AndRetainsHistory` | Messaging |
| AC-08 | `TerminalOutcome_ProducesWeightedSignal` | Integration |
| AC-09 | `ApplicationReadOrWrite_WithoutOwnerScope_IsRefused` | API |
| AC-10 | `ImpossibleTransition_IsRefusedWithRemedy_ApplicationUnchanged` | **Matrix** + API |

## The transition matrix suite

A theory over the Cartesian product of `ApplicationStatus × ApplicationStatus` — 49 pairs including
self-transitions — asserted against the contract table. For each pair:

1. Build an application in the `from` status with a known history length.
2. Attempt the transition.
3. Assert permitted or refused **exactly as the table says**.
4. If permitted: status changed, history length +1, `last_activity_at` advanced.
5. If refused: status unchanged, **history length unchanged** (AC-02), and the refusal names a remedy.

Enumerating the product rather than hand-writing cases is the point: it is the difference between
testing the rules and testing the cases someone thought of. Adding a status to the enum
automatically expands the suite and fails until the table is updated.

## Edge cases / error paths

- Action on a job with no application → created lazily in `New`, then transitioned in one operation.
- Two rapid taps on the same button → idempotent; the second produces no second transition.
- `Applied` twice → permitted as a re-affirmation but `applied_at` is set only once.
- `Interview → Interview` (a second round) → permitted; the transition record is how rounds are visible.
- Job closes while the application is in `Interview` → marked, status untouched (AC-07).
- Job closes while the application is `Saved` → marked; a reminder suggests dropping or applying elsewhere.
- Application archived after 180 days terminal → absent from the pipeline view, still retrievable by id.
- Reminder due on the same day the Owner acts → the sweep sees the fresh `last_activity_at` and skips.
- Threshold changed in configuration → takes effect on the next sweep with no per-application rescheduling (SAD §4 S3).
- Note of 5 000 characters → refused at the cap with a clear message.
- Note containing a secret-shaped string → stored, never logged; the log records length only.
- 200 applications in the pipeline view → under 500 ms, covered by the partial index.

## Test data

- `ApplicationBuilder` producing applications in any status with a synthetic history.
- `FakeClock` for every threshold and sweep test — a seven-day suppression test runs in milliseconds.
- A fixture pipeline of 200 applications across all statuses for the latency assertion.

## NFR validation

- Pipeline view < 500 ms for 200 applications → benchmark plus a query plan assertion that
  `idx_applications_pipeline` is used.
- Status update < 1 s → callback latency ceiling.
- **Zero duplicate reminders** → the sweep run over seven consecutive simulated days asserting exactly
  one message.
- History completeness 100% → asserted on every permitted transition, not sampled.
- Transition legality 100% → the matrix suite covers every pair.

## CI

- **PR:** all levels. The matrix suite is fast and always runs.
- **On enum change:** adding a status expands the matrix automatically and fails until the contract
  table and the rules agree — which is the intended forcing function.

## Related

[[../../engineering/testing-strategy]] · [[contracts/application-api]] §Transition matrix · [[sad]] §10
