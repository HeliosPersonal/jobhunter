---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f5-daily-digest-telegram, mvp, jobhunter]
---

# Test plan — f5-daily-digest-telegram

> Two suites carry this feature: the **rendering corpus** (every layout, including hostile input) and
> the **duplicate-delivery suite** (QG-2). Both run entirely against a fake notifier, so 200 layout
> cases cost seconds.

## Levels

| Level | Scope | Network | Docker | Tooling |
|---|---|---|---|---|
| Unit | Escaping, truncation, card-key derivation, short-id HMAC, pacing arithmetic | No | No | xUnit |
| **Rendering corpus** | Every layout and every degraded variant, against a fake notifier | No | No | xUnit + snapshot |
| Integration | Digest assembly, apply-link verification, suppression summary | No | Yes | Testcontainers |
| **Delivery** | Interrupted delivery, resume, exactly-once | No | Yes | Testcontainers + fake notifier |
| Callback | Action handling, acknowledgement, stale card, unauthorised chat | No | Yes | Testcontainers |
| Schedule | 07:00 across DST; the 06:45 deadline paths | No | No | xUnit + `FakeClock` |
| Live smoke | One real message to a test chat, manual, pre-release only | **Yes** | No | Manual checklist |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `Header_StatesCountsBestMatchAndSalary_InSixLinesOrFewer` | Rendering |
| AC-02 | `CardWithNoReasons_IsNotPresented` | Integration |
| AC-03 | `Action_IsRecorded_Acknowledged_AndKeyboardUpdated` | Callback |
| AC-04 | `InterruptedDelivery_ResumesWithoutResending` | **Delivery** |
| AC-05 | `NoNewJobs_StillDeliversDigest_StatingSoPlainly` | Schedule + Rendering |
| AC-06 | `IncompleteAnalysis_DeliversOnTime_StatingWhatIsMissing` · `CostAborted_DeliversReduced_WithWarning` | Schedule |
| AC-07 | `Digest_ReportsSuppressedCountAndReasons` | Integration + Rendering |
| AC-08 | `Action_CapturesSignalWithJobFactsAtThatMoment_InSameTransaction` | Callback |
| AC-09 | `ActionOnClosedOrMissingJob_TellsOwnerPlainly_AndRecordsNothingInvalid` | Callback |
| AC-10 | `UpdateFromUnauthorisedChat_IsDroppedBeforeRouting_AndLogged` | Callback |
| AC-11 | `CardWithUnreachableApplyDestination_IsNotPresented` | Integration |
| AC-12 | `EachSupportedCommand_ReturnsScannableOutput` | Callback + Rendering |

## The rendering corpus

`tests/JobHunter.Telegram.Tests/Data/rendering/` — snapshot tests over a fake `INotifier` that
captures `RenderedMessage` values. Roughly 200 cases across:

**Layout**
- Header with 0, 1, 9, 10 cards; with and without a salary statistic; with and without suppression.
- Card with 1, 2, 3 reasons; with published salary, with estimate, with neither.
- Card with `Unknown` company stage (line omitted, not printed as "Unknown").
- Footer with each combination of suppressed / carried-over / degraded lines.
- All four degraded-day variants from [[contracts/telegram-messages|the contract]].

**Hostile input** — every row of the contract's escaping table, plus:
- A title that is entirely markdown control characters.
- A company name containing a zero-width joiner.
- A reason containing a URL with parentheses.
- A grapheme cluster (flag emoji, family emoji) exactly at the truncation boundary.

**Splitting**
- A digest whose cards total just under, exactly, and just over 4096 characters.
- Assert splits never occur mid-card.

Snapshots are committed and reviewed in diffs — a layout change should be visible in a PR, because
layout *is* the product here.

## The duplicate-delivery suite

QG-2, and the reason [[adr/0002-delivery-idempotence|ADR-F5-0002]] exists.

| Case | Assert |
|---|---|
| Clean delivery of 10 cards | 12 messages sent (header + 10 + footer), 12 delivery-log rows |
| Kill after card 3, restart | Exactly 7 more cards sent; 12 rows total; no card sent twice |
| Kill between send and log write | The card is re-sent once — an at-least-once send with a duplicate is preferable to a lost card, and the window is one statement wide |
| Retry the whole delivery after success | Zero messages sent |
| `/digest` command after delivery | The digest renders again but **writes no delivery-log rows** and re-sends nothing through the delivery path |
| Two delivery handlers racing | The unique constraint means one wins per card; total sends equal card count |
| Telegram returns 429 mid-delivery | Pacing honours `retry_after`; delivery completes; no duplicates |
| Telegram returns 400 for one card | That card is logged as failed, the rest deliver, and the digest is not abandoned |

## Edge cases / error paths

- Ten cards all with identical scores → ordering is stable and deterministic.
- A card's job closes between assembly and delivery → apply-link verification at assembly catches
  most; a tap on a closed job is handled by AC-09.
- The narrative call is over budget → template fallback, `narrative_source = Template`, digest ships.
- The narrative returns prose containing markdown → escaped like any other dynamic value.
- Fewer than three jobs have a salary → `avg_salary_usd` is null and the line is omitted rather than
  showing a misleading average of two.
- All ten cards suppressed → the header reports 0 shown and the footer explains; this is a valid digest.
- The Owner taps the same button twice quickly → the second is idempotent and re-acknowledges.
- The Owner taps `Applied` on a job already `Ignored` → the transition is legal and recorded (F6 owns
  the rules); the keyboard updates.
- A short id from a digest 30 days old → resolves if the digest is retained; otherwise a clear message.
- DST transition night → 07:00 local is asserted on both the spring-forward and autumn-back dates.
- Bot token rotated mid-day → long polling reconnects; delivery resumes from the log.

## Test data

- `FakeNotifier` capturing `RenderedMessage` with the chat id, text, keyboard and ordering.
- `DigestBuilder` producing digests with a configurable card count and characteristics.
- `Data/hostile-strings.yaml` — the escaping adversarial set, shared with the corpus.
- `FakeClock` for every schedule and DST test; no test waits on real time.
- A recorded set of Telegram API responses (200, 400, 429) for the transport tests.

## NFR validation

- Delivery at 07:00 ±3 min → schedule test with `FakeClock`, including both DST transitions.
- Header ≤ 6 lines → asserted directly in the corpus.
- Card count = score ≥ 70 capped at 10 → assembly test at the boundaries (69, 70, and 11 qualifying).
- Callback acknowledgement < 1 s → asserted on the handler path with a stopwatch ceiling.
- **Duplicate deliveries: 0** → the duplicate-delivery suite, every case.
- Apply-link liveness 100% → integration test with a fake HTTP layer returning 200, 404, 410 and a timeout.
- Rendering failures 0 → the whole corpus must render without an exception and without an unescaped
  control character.

## CI

- **PR:** unit, rendering corpus, integration, delivery, callback, schedule.
- **Pre-release:** the manual live-smoke checklist — one real digest to a test chat, verifying that
  buttons work in a real client. Some things only break in the real app.

## Related

[[../../engineering/testing-strategy]] · [[contracts/telegram-messages]] · [[sad]] §10 ·
[[../../operations/runbooks|R1]]
