# T07 — AnthropicBatchClient

**Layer:** claude · **Deps:** T03 · **Est:** L · **Owner:** Viacheslav

## What

The adapter: submit a JSONL batch with tool-use schema constraints, poll status, stream
JSONL results. All request building and response parsing lives in `internal static` pure methods so
the whole adapter is testable against saved payloads with zero network — the `wisewizard` pattern.

## Done when

- Submit, status and streamed retrieval all work against recorded payloads with zero network.
- The provider batch id is returned from submit and nothing else is required to resume.
- Results stream — a 150-item JSONL result set is processed without materialising it.
- Per-item provider errors surface as `BatchResultItem.ProviderError`, not as exceptions.
- Transport failures retry through the shared resilience handler; a 4xx does not retry.
- The API key never appears in a log, an exception message or a span attribute.

## Links

[[../sad]] §5 · [[../../../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]]
