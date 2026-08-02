# T14 — Ollama cheap-tier fallback adapter

**Layer:** claude · **Deps:** T07 · **Est:** S · **Owner:** Viacheslav

## What

A second `ILlmBatchClient` implementation targeting Ollama on the helios cluster, used as the
cheap-tier fallback when the Anthropic budget is exhausted or the provider is offline
([[../../../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]]). It reuses
`AnthropicBatchClient`'s request-building and tolerant-parsing machinery; only the transport and the
response shape differ. Selection is a configuration decision, not a fork in the pipeline — the
orchestrator submits through the same port.

## Done when

- The adapter submits, polls and streams results against recorded Ollama payloads with zero network.
- It surfaces per-item failures as `BatchResultItem.ProviderError`, matching the Anthropic adapter's contract.
- Fallback selection is a configuration switch; the orchestrator and cost gate are unchanged.
- Its absence degrades quality, not availability — a missing Ollama endpoint never fails a Run.
- The enrichment output parses through the same `TolerantJsonParser` and schema as the Anthropic tier.

## Links

[[../sad]] §3 · [[../sad]] §5 · [[../../../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]]
