---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, contracts, events, architecture, jobhunter]
---

# Event catalog

> Every message that crosses a stage boundary. All live in `JobHunter.Contracts`, are `record`
> types, are named in the past tense, and carry no behaviour.
> Transport and delivery guarantees: [[../00-overview/adr/0002-rabbitmq-wolverine-transport|ADR-0002]],
> [[../00-overview/adr/0007-transactional-outbox|ADR-0007]].

---

## 1. Rules

1. **Past tense, always.** `JobDiscovered`, not `DiscoverJob`. A command that is not yet a fact does
   not belong on the bus — it is a method call.
2. **Versioned by type name.** A breaking change creates `JobDiscoveredV2`; the old handler stays
   until the queue drains. Never mutate a shipped contract's meaning.
3. **Ids, not payloads.** Events carry `JobId`, `RunId` and the few fields a consumer needs to
   decide *whether* to act. The consumer reads the rest from PostgreSQL. This keeps messages small
   and keeps PostgreSQL the single source of truth.
4. **Every event carries `RunId` where a Run exists** and `OccurredAt` always — these become the
   trace and log correlation keys.
5. **Every consumer is idempotent**, keyed as listed in the table below. At-least-once delivery is
   assumed, never worked around.
6. **Queue name** = `{MessageType.FullName}.{consuming-deployable}`, auto-provisioned, one dead-letter
   queue per stage. So `RankingCompleted`, consumed by the Worker's `DigestAssembler`, owns
   `….jobhunter-worker`, not `…-telegram`.

---

## 2. The pipeline

```mermaid
graph LR
  subgraph Ingestion
    A[SourceFetchRequested] --> B[RawPostingIngested]
    B --> C[JobNormalized]
    C --> D[JobDiscovered]
    C --> D2[JobDuplicateDetected]
    A -.-> SQ[SourceQuarantined]
    B -.-> JC[JobClosed]
  end
  subgraph Intelligence
    D --> E[RunStarted]
    CV[CvVersionActivated] --> E
    E --> F[EnrichmentBatchSubmitted]
    F --> G[EnrichmentCompleted]
    G --> H[MatchingBatchSubmitted]
    H --> I[MatchingCompleted]
    I --> J[RankingCompleted]
  end
  subgraph Output
    J --> K[ResearchRequested]
    K --> RC[ResearchCompleted]
    J --> L[DigestReady]
    RC --> L
    DDD[DigestDeliveryDue] --> M[DigestDelivered]
    M --> N[OwnerActionRecorded]
    N --> AS[ApplicationStatusChanged]
    J --> P[JobIndexRequested]
    JC --> P
    O[PreferenceModelUpdated] --> J
  end
  E -.-> X[RunFailed]
  E -.-> Y[RunCostAborted]
```

`PreferenceModelUpdated` is emitted by a weekly Hangfire fitting job, not as a consequence of an
`OwnerActionRecorded` tap; it feeds the next Run's ranking.

> **Open gap:** `OwnerActionRecorded` is defined in `JobHunter.Contracts` and consumed by
> `OwnerActionHandler`, but it currently has **no producer in `src/`** — the Applied tap never
> publishes it yet. The consumer is wired ahead of the producer.

`DigestReady` is an assembled-and-persisted marker with no consumer — the digest is built and held
earlier in the day. Delivery is a separate slot: the Worker's `DigestDeliveryTrigger` Hangfire cron
fires `DigestDeliveryDue` at 07:00 Europe/Kyiv, and the Worker's `DeliveryHandler` consumes it and
sends. This decouples "assembled" from "delivered at exactly 07:00" (F5 SAD §6.3, ADR-F5-0001).

---

## 3. Catalog

| Event | Published by | Consumed by | Idempotency key | Payload |
|---|---|---|---|---|
| `SourceFetchRequested` | `DiscoveryCycleHandler` (off a Hangfire 6-hourly tick) | `FetchSourceHandler` | `(SourceId, WindowStart)` | `SourceId`, `CompanyId`, `AtsKind`, `WindowStart`, `OccurredAt` |
| `RawPostingIngested` | `FetchSourceHandler` | `NormalizationHandler` | `ContentHash` | `RawPostingId`, `SourceId`, `CompanyId`, `ContentHash`, `OccurredAt` |
| `JobNormalized` | `NormalizationHandler` | `DeduplicationHandler` | `RawPostingId` | `JobId` (candidate), `RawPostingId`, `CompanyId`, `Fingerprint`, `OccurredAt` |
| `JobDiscovered` | `DeduplicationHandler` | `JobIndexRequestTranslator` *(Run enrolment is driven by the daily `StartDailyRun` tick, not off this event)* | `JobId` | `JobId`, `CompanyId`, `Title`, `FirstSeenAt` |
| `JobDuplicateDetected` | `DeduplicationHandler` | *(none — `jobhunter.jobs.deduplicated` recorded inline via `Telemetry`)* | `(CanonicalJobId, DuplicateRawPostingId)` | `CanonicalJobId`, `DuplicateRawPostingId`, `SourceId` |
| `JobClosed` | `ClosureSweepHandler`, `JobLifecycleHandler`, `UnreachableApplyLinkHandler` | `JobClosureHandler`, `JobIndexRequestTranslator` | `(JobId, ClosedAt)` | `JobId`, `ClosedAt`, `Reason` |
| `RunStarted` | `RunOrchestrator` (off the daily `StartDailyRun` tick from `DailyRunTrigger`) | *(none — audit marker; the Run advances via the internal `EnrichmentSubmissionDue` command)* | `RunId` | `RunId`, `CutoffFrom`, `CutoffTo`, `CeilingUsd` |
| `EnrichmentBatchSubmitted` | `EnrichmentSubmitHandler` | *(none — audit marker; results are collected via the `BatchPollDue` tick)* | `(RunId, Stage, Tier)` | `RunId`, `BatchId`, `ProviderBatchId`, `ItemCount` |
| `EnrichmentCompleted` | `BatchResultProcessingHandler`, `BatchPollHandler` (also `EnrichmentSubmitHandler`/`RunOrchestrator` on the empty-scope short-circuit) | `MatchingSubmitHandler` | `(RunId, Stage)` | `RunId`, `Succeeded`, `Failed`, `CostUsd`, `OccurredAt` |
| `MatchingBatchSubmitted` | `MatchingSubmitHandler` | *(none — audit marker; results are collected via the `MatchingPollDue` tick)* | `(RunId, Stage, Tier)` | `RunId`, `BatchId`, `ProviderBatchId`, `ItemCount` |
| `MatchingCompleted` | `MatchingResultProcessingHandler`, `MatchingPollHandler` (also `MatchingSubmitHandler` on the empty-scope short-circuit) | `RankingHandler` | `(RunId, Stage)` | `RunId`, `Succeeded`, `Failed`, `CostUsd`, `OccurredAt` |
| `RankingCompleted` | `RankingHandler` | `DigestAssembler` *(research + search-index fan-out designed, not yet wired)* | `RunId` | `RunId`, `RankedCount`, `SuppressedCount`, `TopJobIds`, `OccurredAt` |
| `ResearchRequested` | *(designed — not yet published)* | *(none yet — research fan-out not wired)* | `(RunId, CompanyId)` | `RunId`, `CompanyId`, `Reason` |
| `ResearchCompleted` | `ResearchFeedback.CompletedEvent` *(mints it; not yet published)* | *(none yet)* | `(RunId, CompanyId)` | `RunId`, `CompanyId`, `ResearchId`, `ClaimCount` |
| `DigestReady` | `DigestAssembler` | *(none — assembled marker)* | `RunId` | `RunId`, `DigestId`, `CardCount`, `GeneratedAt` |
| `DigestDeliveryDue` | Hangfire (`DigestDeliveryTrigger`, 07:00 Europe/Kyiv) | `DeliveryHandler` (Worker) | `DueAt` (per-card `(RunId, ChatId, CardKey)`) | `DueAt` |
| `DigestDelivered` | `DeliveryHandler` | *(none — `jobhunter.digest.cards` recorded inline via `Telemetry`)* | `(RunId, ChatId)` | `RunId`, `ChatId`, `CardsDelivered`, `DeliveredAt` |
| `OwnerActionRecorded` | *(not yet produced — see note below)* | `OwnerActionHandler` (which stages outcome signals inline via `OutcomeSignalPublisher`) | `(JobId, Action, OccurredAt)` | `JobId`, `Action` (`Open`\|`Ignore`\|`Save`\|`Applied`), `ChatId`, `OccurredAt` |
| `ApplicationStatusChanged` | `OwnerActionHandler` *(`ChangeApplicationStatusHandler` and `JobClosureHandler` mutate status request-driven; they stage outcome signals via `OutcomeSignalPublisher` rather than publishing this event)* | *(none yet — not consumed off the bus)* | `(ApplicationId, ToStatus, OccurredAt)` | `ApplicationId`, `JobId`, `FromStatus`, `ToStatus` |
| `PreferenceModelUpdated` | `PreferenceLearner` | *(none — `RankingHandler` reads the latest model on the next Run, not off the bus)* | `ModelVersion` | `ModelId`, `Version`, `SignalCount`, `FittedAt` |
| `JobIndexRequested` | `JobIndexRequestTranslator` (from `JobDiscovered`/`JobClosed`) | `SearchIndexingHandler` | `(JobId, Revision)` | `JobId`, `Operation` (`Upsert`\|`Delete`), `OccurredAt` |
| `CvVersionActivated` | *(designed — not yet published)* | *(none yet)* | `CvVersionId` | `ProfileId`, `CvVersionId`, `ActivatedAt` |
| `RunFailed` | *(designed — not yet published)* | *(none yet)* | `RunId` | `RunId`, `Stage`, `Reason`, `FailedAt` |
| `RunCostAborted` | `EnrichmentSubmitHandler`, `MatchingSubmitHandler` (pre-submission ceiling check) | *(none yet — a reduced digest still ships; no `Handle` consumer)* | `RunId` | `RunId`, `Stage`, `SpentUsd`, `CeilingUsd` |
| `SourceQuarantined` | `FetchSourceHandler` | *(none yet — `jobhunter.source.failures` recorded inline via `Telemetry`; Owner-alert consumer not wired)* | `(SourceId, QuarantinedAt)` | `SourceId`, `CompanyId`, `ConsecutiveFailures`, `LastStatus` |

---

## 4. Contract shape

```csharp
namespace JobHunter.Contracts.Pipeline;

/// <summary>A Job passed deduplication and is canonical. Stage 3 → Run scope.</summary>
public sealed record JobDiscovered(
    Guid JobId,
    Guid CompanyId,
    string Title,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset OccurredAt);

/// <summary>An Anthropic batch was accepted. The poller owns it from here.</summary>
public sealed record EnrichmentBatchSubmitted(
    Guid RunId,
    Guid BatchId,
    string ProviderBatchId,
    int ItemCount,
    DateTimeOffset OccurredAt);
```

Serialisation is `System.Text.Json` with a source-generated context in `JobHunter.Contracts`, so no
reflection at runtime and a compile-time error on an unserialisable member.

---

## 5. Failure handling per stage

| Condition | Policy |
|---|---|
| Transient HTTP failure fetching a source | 3 retries, exponential backoff, then `SourceQuarantined` — the Run continues |
| Malformed ATS payload | Log, record in `source_fetch_log`, skip the item, continue the batch |
| Malformed LLM item | Record `batch_items.parse_error`, retry once next Run at cheap tier — never fails the Run ([[../00-overview/adr/0006-structured-output-contract\|ADR-0006]]) |
| Batch still `InProgress` past the deadline | Deliver a partial digest at 07:00 with an explicit note; the Batch keeps polling for tomorrow |
| Cost estimate exceeds the remaining ceiling | `RunCostAborted`; reduced digest; **never** silent truncation (invariant 6) |
| Handler throws unexpectedly | 3 Wolverine retries, then dead-letter the message and publish `RunFailed`; the Owner is notified |
| Telegram send fails | Retry with backoff; `delivery_log` prevents duplicates on retry (invariant 8) |
| Typesense unavailable | Index operations are best-effort and re-queued; the digest never depends on the index |

---

## Related

- [[data-model]] · [[../00-overview/sad]] §6 · [[../CONTEXT]] §2
- [[../00-overview/adr/0002-rabbitmq-wolverine-transport|ADR-0002]] · [[../00-overview/adr/0007-transactional-outbox|ADR-0007]]
