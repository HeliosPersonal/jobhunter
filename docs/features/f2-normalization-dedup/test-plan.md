---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f2-normalization-dedup, mvp, jobhunter]
---

# Test plan — f2-normalization-dedup

> The centre of gravity is the **labelled dedup corpus**. A single false merge fails the build.

## Levels

| Level | Scope | Docker | Tooling |
|---|---|---|---|
| Unit | Title normalisation, seniority extraction, location parsing, salary parsing, remote resolution, fingerprint | No | xUnit |
| Corpus | ≥ 200 labelled pairs asserting merge / do-not-merge | No | xUnit + `Data/dedup-corpus.yaml` |
| Fixture | End-to-end normalisation of each provider's fixtures from F1 | No | xUnit |
| Integration | `ON CONFLICT` dedup path, alias recording, closure sweep, reprocessing | Yes | Testcontainers |
| Messaging | Two-stage handler chain, idempotency, concurrent same-fingerprint race | Yes | Testcontainers |
| Property | Fingerprint determinism under culture and clock variation | No | xUnit |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `Normalize_<Provider>Payload_ProducesCompleteCanonicalJob` ×5 | Fixture |
| AC-02 | `SameOpeningFromTwoSources_ProducesOneJob_AndTwoAliases` | Integration |
| AC-03 | `SameTitleDifferentLocation_ProducesTwoJobs` | Corpus + Integration |
| AC-04 | `PayloadMissingRequiredField_RecordsFailure_AndDoesNotHaltBatch` | Messaging |
| AC-05 | `TitleNormalization_ExtractsSeniority_AndPreservesPublishedTitle` | Unit |
| AC-06 | `JobUnseenForTwoCycles_IsClosed_AndExcludedFromDigestAndSearch` | Integration |
| AC-07 | `ClosedJobReappearing_ReopensSameJob_NotADuplicate` | Integration |
| AC-08 | `EveryContributingPosting_IsListedAsAnAlias` | Integration |
| AC-09 | `Reprocess_RecomputesFromStoredPayloads_WithZeroNetwork_AndStableIds` | Integration |
| AC-10 | `NearDuplicates_AreGroupedForDisplay_NeitherDiscarded` (grouping now in F5 assembly) | Corpus + Integration |
| AC-11 | `Reprocess_WithoutOperatorScope_IsRefused` | Smoke |

## The dedup corpus

`tests/JobHunter.Application.Tests/Data/dedup-corpus.yaml` — ≥ 200 labelled pairs, each
`{a, b, expect: merge | distinct, why}`. Categories, with the adversarial ones deliberately
over-represented:

| Category | Example | Expect |
|---|---|---|
| Same job, two boards | Greenhouse + company careers page, identical title and location | merge |
| Same job, cosmetic title difference | `Senior Backend Engineer` vs `Sr. Backend Engineer` | merge |
| Same job, decorated title | `Backend Engineer (Remote)` vs `Backend Engineer`, same location set | merge |
| Same job, reposted 14 days later | new external id, new posted date, everything else identical | merge |
| **Different city** | `Backend Engineer` Berlin vs Munich | **distinct** |
| **Different seniority** | `Backend Engineer` vs `Staff Backend Engineer` | **distinct** |
| **Different employment type** | same title, contract vs permanent, same location | **distinct** — proves the title carries the distinction, or the corpus catches that it does not |
| **Different team, same title** | `Software Engineer, Payments` vs `Software Engineer, Growth` | **distinct** |
| **Different company, same title** | Stripe vs Adyen, `Backend Engineer`, Berlin | **distinct** |
| Multi-location superset | `[Berlin, Munich]` vs `[Berlin]` | **distinct** — a differing set is a differing job |
| Near-duplicate, ungrouped | `Backend Engineer` vs `Backend Developer`, same company and city | **distinct**, but grouped for display (AC-10) |

**The corpus grows by defect.** Any pair the Owner reports as wrongly merged or wrongly split is
added with its label before the fix is merged.

## Edge cases / error paths

- Empty location set (fully remote, none published) → legal; the fingerprint uses the empty set consistently.
- Location published only as free text such as `Remote - EMEA` → parsed to a region-only entry;
  unparseable text is retained verbatim and contributes to the fingerprint, so it stays deterministic.
- Salary published as `Competitive` → `salary_raw` set, structured fields null, never coerced to zero.
- Salary range inverted (max below min) → swapped, and the anomaly logged.
- Salary in a currency with no ISO code → `salary_raw` only.
- Title empty or whitespace → normalisation failure (AC-04), not a job with an empty title.
- Description empty → legal. A job with no description is still a job; it will rank poorly, which is correct.
- Two consumers process the same fingerprint concurrently → one row, two aliases, no exception.
- The same raw posting is replayed → handler idempotent on the raw posting id; no second alias.
- A job's source is quarantined → closure suspended for that job (SAD §11 D4), so a provider outage does not empty the digest.
- Turkish culture (`tr-TR`) → the dotless-i lowercasing trap; asserted explicitly in the property suite.

## Test data

- Provider fixtures reused from F1's `Fixtures/` — one corpus, two consumers.
- 200 labelled titles in `Data/titles.yaml` for seniority accuracy (≥ 95%).
- `JobBuilder` and `RawPostingBuilder` in `JobHunter.TestKit`.
- Frozen fingerprint expectations in `Data/fingerprints.json` — 50 payloads with their expected
  hashes. Changing any of them is a deliberate migration, never an accident (QG-2).

## NFR validation

- ≥ 500 postings/min → messaging benchmark over 5 000 fixture postings.
- Fingerprint under 1 ms → BenchmarkDotNet, asserted as a ceiling in CI rather than a reported number.
- **False-merge rate 0** → corpus test; any `distinct` pair sharing a fingerprint fails the build.
- False-split ≤ 5% → corpus test; a `merge` pair not sharing a fingerprint counts toward the budget.
- Seniority ≥ 95% → the 200-title set; the test fails below 190.
- Location coverage ≥ 90% → asserted over the full provider fixture set.
- Reprocess ≥ 5 000/min with zero network → benchmark with an HTTP handler that throws on any call.

## CI

- **PR:** all levels. The corpus and property suites are fast and always run.
- **On corpus change:** the diff must state why each new pair was added — a corpus entry without a
  reason is not reviewable.

## Related

[[../../engineering/testing-strategy]] · [[PRD]] §5 · [[sad]] §10
