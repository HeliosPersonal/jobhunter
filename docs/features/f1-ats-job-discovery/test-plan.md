---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f1-ats-job-discovery, mvp, jobhunter]
---

# Test plan — f1-ats-job-discovery

> Every adapter is tested against **recorded fixtures with zero network**. Live provider calls exist
> only in the weekly contract suite, which is alert-only and never gates a PR.

## Levels

| Level | Scope | Network | Docker | Tooling |
|---|---|---|---|---|
| Unit | Canonical domain, content hashing, token-bucket arithmetic, quarantine state machine, robots parsing | No | No | xUnit |
| Fixture | All five adapters against recorded payloads: happy, empty, malformed, huge, paginated | No | No | xUnit + `Fixtures/` |
| Integration | Repositories, the `ON CONFLICT` dedup path, immutability, fetch-log completeness | No | Yes | Testcontainers |
| Messaging | Cycle fan-out, per-source isolation, event emission only on change | No | Yes | Testcontainers |
| Detection | Binding detection over a 50-company labelled set with recorded probe responses | No | No | xUnit |
| Contract | Live provider endpoints still match the consumed shape | **Yes** | No | Weekly, alert-only |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `Cycle_FetchesEachActiveSource_ExactlyOnce` | Messaging |
| AC-02 | `UnchangedContent_CreatesNoNewRow_AndBumpsLastSeen` · `UnchangedContent_PublishesNoEvent` | Integration, Messaging |
| AC-03 | `Detection_WithSingleRespondingCandidate_RecordsBindingWithEvidence` · `Detection_WithNoCandidate_RecordsNoBoardFound` | Detection |
| AC-04 | `Detection_WithMultipleCandidates_RecordsAmbiguity_AndLeavesCompanyInactive` | Detection |
| AC-05 | `Redetection_WhenProviderChanges_RetiresOldBinding_AndKeepsJobsWithCompany` | Detection + Integration |
| AC-06 | `RobotsDisallowedPath_IsNeverFetched_AndDecisionIsRecorded` | Unit + Fixture |
| AC-07 | `RetryAfter_IsHonouredExactly_AndNeverShortened` | Unit |
| AC-08 | `TwoConsecutiveFailures_Quarantines_NotifiesOnce_AndLeavesOtherSourcesUnaffected` | Messaging |
| AC-09 | `DegradedSources_AreReportedToTheDigest` | Integration |
| AC-10 | `RawPosting_HasNoUpdatePathForPayload` | Integration + Architecture |
| AC-11 | `EveryFetchAttempt_ProducesExactlyOneLogRow_IncludingFailures` | Integration |
| AC-12 | `RegistryMutation_WithoutOperatorScope_IsRefused` | Smoke |

## Fixture corpus

Committed under `src/JobHunter.Scrapers/Fixtures/<provider>/`, recorded once from live endpoints and
then frozen. Every provider has at minimum:

| Fixture | Asserts |
|---|---|
| `happy-20-postings.json` | Field mapping, `external_id` extraction, content hashing |
| `empty-board.json` | Zero postings is a success, not an error |
| `single-posting.json` | No off-by-one in streaming |
| `malformed-truncated.json` | Parse error is recorded, other postings in the batch survive |
| `missing-optional-fields.json` | Nulls do not throw; optional means optional |
| `unicode-and-html.json` | Non-Latin titles and HTML descriptions survive round-trip |
| `paginated-page1/2.json` | Pagination is followed to exhaustion |
| `oversized.json` (> 10 MB) | Rejected without buffering |

**Rule:** any payload shape that ever caused a production failure is added here before the fix is
merged. The fixture corpus is the regression suite.

## Edge cases / error paths

- Board returns 200 with an HTML error page → parse error recorded, source not quarantined on first occurrence.
- Provider returns the same `external_id` twice in one response → last wins, one row, logged as a provider anomaly.
- `external_id` absent → posting skipped and counted; a board where all ids are absent is a parse error, not silent zero.
- Company domain resolves to a private IP → refused by the SSRF guard, recorded, never fetched.
- `robots.txt` unreachable → treated as *allow* (the permissive reading), cached briefly, retried next cycle.
- `robots.txt` malformed → treated as *disallow* for safety, and reported.
- Two cycles overlap because one ran long → the second finds sources with a recent `last_fetched_at` and skips them.
- Quarantine expires mid-cycle → source is picked up on the next cycle, not retried immediately.
- A source is deleted while its fetch message is in flight → handler exits cleanly, logs, does not fail the cycle.
- 400-posting board → streamed, memory stays bounded, asserted with a memory ceiling in the test.

## Test data

- 50-company labelled detection set in `tests/JobHunter.Scrapers.Tests/Data/detection-set.yaml`,
  each with expected `ats_kind`, `board_token` and expected outcome (including deliberate
  no-board and ambiguous cases).
  - **Provenance / construction:** the 50 companies are drawn from the curated seed list
    ([[../adr/0001-company-registry-seeding|F1-0001]]), sampled to cover all five ATS kinds plus the
    two negative outcomes (no-board, ambiguous) in rough proportion to their real frequency. Each
    entry's expected `ats_kind`/`board_token` is hand-labelled once from a recorded live probe, and
    the recorded probe response is committed alongside so the set is hermetic and reproducible. The
    set grows by defect: any company whose binding was ever mis-detected in production is added with
    its recorded response before the fix merges.
- `CompanyBuilder`, `BindingBuilder`, `SourceBuilder` fluent factories in `JobHunter.TestKit`.
- `FakeClock` drives quarantine expiry and rate-bucket refill — no test waits on real time.
- Redis is faked with an in-memory `IRateLimiter` for unit tests; the real Redis path is covered once,
  in an integration test.

## NFR validation

- Cycle < 20 min for 300 companies → simulated with 300 fixture-backed sources and a fake clock;
  asserts the concurrency degree and total request count rather than wall clock.
- Rate ≤ 1 req/s per host → token-bucket unit test asserts the 61st request within a minute is deferred.
- Timeouts → policy configuration asserted; a fixture source that hangs is cancelled at 30 s.
- 10 MB cap → oversized fixture rejected before full buffering (asserted by peak allocation).
- Concurrency ≤ 8 → a counting handler asserts the maximum observed in-flight count.
- 0% unchanged re-store → integration test fetches the same payload twice, asserts one row.
- Detection ≥ 95% → the labelled set; the test fails below 47 of 50.

## CI

- **PR:** unit, fixture, integration, messaging, detection.
- **Weekly:** contract suite against live endpoints; failure opens an issue and alerts, but does not
  break the build — a provider changing its API is news, not a regression in our code.

## Related

[[../../engineering/testing-strategy]] · [[PRD]] §5 · [[sad]] §10
