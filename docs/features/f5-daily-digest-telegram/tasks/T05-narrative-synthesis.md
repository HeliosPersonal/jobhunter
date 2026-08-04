# T05 — Narrative synthesis with template fallback

**Layer:** claude · **Deps:** T03 · **Est:** M · **Owner:** Viacheslav

## What

One deep-tier synthesis call through F3's machinery producing the market note, plus the
template fallback used when the call is unavailable or over budget. The narrative is optional by
design — a provider outage must not cost the digest.

## Done when

- A successful call produces a narrative and sets `narrative_source = Model`.
- An unavailable provider or an exhausted budget produces a template narrative and sets `narrative_source = Template` — the digest still ships.
- Narrative text is escaped like any other dynamic value.
- The synthesis submission is ledgered and ceiling-checked like every other batch.
- The prompt is snapshot-tested so a change is visible in a diff.

## Delivered

Design decision: **Option A — bounded best-effort, inline** ([[../adr/0001-never-delay-the-digest|ADR-F5-0001]]).
The synthesiser makes one deep-tier call from inside digest assembly, polls it within a short budget, and
falls back to the deterministic template the moment anything is off. A market note is a nicety; a nicety
must never delay or fail the 07:00 digest.

- **`NarrativeInput`** (Domain/Reporting) — the aggregate facts the note is synthesised from: only counts
  and one salary statistic, the numbers already destined for the digest header and footer. It carries
  **nothing about the Owner** — no CV text, no card reason, no job description — so the CV still crosses
  exactly one boundary (F4's match prompt) and it is not this one. `HasSomethingToSay` is false for a dead
  day, which short-circuits to the template with no run lookup and no spend.
- **`NarrativeResult`** (Application/Reporting) — the outcome: the text, the `NarrativeSource`, and — only
  for a model note — the prompt version. The two factories make an ill-formed pairing unconstructible: a
  `Model` result always carries both a non-blank narrative and a prompt version; a `Template` result never
  carries a version (a template made no model call, so a version would be fabricated provenance). This
  mirrors the `Digest` constructor's invariant exactly.
- **`NarrativeTemplate`** (Application/Reporting) — the deterministic fallback, a pure function of the same
  `NarrativeInput` the model would have seen. It always produces a non-blank sentence, including a calm
  dead-day line, so the digest always has a header to render and the synthesiser never invents one.
- **`INarrativeRequestBuilder` / `NarrativeBatchRequest` / `INarrativeResultParser`** (Domain/Abstractions
  ports) + **`NarrativeRequestBuilder` / `NarrativeResultParser` / `DigestNarrativePrompt` /
  `DigestNarrativeSchema`** (Claude/Prompts) — the versioned prompt (`digest-narrative-v1`), its tool-use
  schema (`record_market_note`, a single required non-blank `narrative` string) and the tolerant parser all
  live in the adapter, so the Application layer stays free of any provider concept (architecture rule 3).
  The prompt system text forbids advice, ranking, history and anything about the reader; the render is
  snapshot-tested so a change is visible in a diff and forces a version bump. Parsing **never throws** — a
  malformed, empty, non-object or blank-`narrative` payload is a recorded failure, not an exception.
- **`LlmBatchClientException`** (Domain/Abstractions) — a Domain-level base on the `ILlmBatchClient` port
  that the adapter's `AnthropicApiException` now derives from, so the synthesiser catches one Domain type
  (plus `HttpRequestException` for a raw transport fault) without reaching across the boundary. It never
  carries secrets (invariant 12).
- **`NarrativeSynthesizer`** (Application/Reporting) — the one `INarrativeSynthesizer`. It prices, ceiling-
  checks and ledgers the call **exactly** like every other batch (invariant 6): the estimate-plus-spend is
  checked against the Run's ceiling *before* the client is touched, and on a breach the client is **not
  called at all** — the template is used, which costs nothing. The Estimated ledger entry is written and
  committed before submission; a re-entry that already committed it reuses it (guarded by
  `HasLedgerEntryAsync`). The whole submit-and-poll cycle runs under a linked, self-cancelling
  `CancellationTokenSource` set to `Timeout`: when it elapses, or the provider faults, or the result does
  not parse, or the batch is provider-side cancelled/expired, the note is abandoned and the template
  answers — no failure escapes as an exception. A spent batch always writes its Actual entry and completes,
  even when the note itself did not parse. Idempotency comes from the unique `(run, Synthesis, Deep)` index:
  a re-entry adopts the existing `Batch` and polls rather than paying again. Unlike enrichment and matching,
  a synthesis batch persists **no `BatchItem` rows** — a job-less, never-retried, never-carried-over note
  has nothing for a per-item row to isolate, and the Domain rightly forbids a job-less `BatchItem`; the
  `Batch` row alone is the idempotency key.
- **`NarrativeSynthesisOptions`** (Application/Reporting) — `Timeout` (default 20 s, the whole submit+poll
  budget) and `PollInterval` (default 2 s), both startup-validated via `.Validate().ValidateOnStart()`.
- **`DigestAssembler` wiring** — assembles the `NarrativeInput` from the counts it already computes and
  awaits the synthesiser, passing the resulting narrative, source and prompt version into the `Digest`.
  Narrative text is escaped like any other dynamic value at render time (T06).

20 synthesiser tests (`Application.Tests/Reporting/NarrativeSynthesizerTests`, zero database, zero network)
covering the model happy path, the ceiling gate proven as an **absence** via `FakeLlmBatchClient.ThrowOnSubmit`
(QG-2), estimate-before-submit ordering, and every fallback (dead day, missing run, provider fault, transport
fault, budget exhaustion, caller cancellation, provider-side cancel, provider item error, parse miss) plus
adopt-don't-resubmit and single-ledger idempotency; 7 prompt tests (snapshot + guardrails), 8 parser tests and
5 request-builder tests (`Claude.Tests/Prompts`); the CV-leakage sentinel scan stays green; solution builds
with zero warnings.

## Links

[[../../f3-claude-batch-enrichment/tasks/T10-enrichment-submit|F3 T10]] · [[../adr/0001-never-delay-the-digest|ADR-F5-0001]]
