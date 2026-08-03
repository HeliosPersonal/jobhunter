# T10 — Extend the technology vocabulary with target-stack coverage

**Layer:** app · **Deps:** T07 · **Est:** M · **Owner:** Viacheslav

## What

T07 shipped a committed vocabulary (`src/JobHunter.Infrastructure/Normalization/technology-vocabulary.yaml`,
loaded by `TechnologyVocabularyLoader` into the pure `TechnologyVocabulary`). It already covers general
languages, frameworks, data stores and infra, but it is thin on the AI-native and platform-engineering
terms the Owner is targeting — so the deterministic tagger and the F7 `Technology` dimension are blind to
the target stack. Audit the existing file and extend it (aliases → canonical, whole-token-safe) so the
target stack tags correctly.

Keywords to ADD or confirm (canonical ← aliases):

- **Agentic / LLM tooling:** MCP ← Model Context Protocol; Claude / Anthropic; OpenAI ← GPT; Gemini;
  Cursor; Vercel AI SDK; Bedrock ← Amazon Bedrock; Azure OpenAI; Vertex AI; LangGraph; Semantic Kernel;
  AutoGen; CrewAI; agent orchestration; tool/function calling; prompt management; LLM eval; guardrails;
  fine-tuning; inference serving. (LangChain, Hugging Face, Ollama already exist — confirm, do not duplicate.)
- **Retrieval / vectors:** RAG; embeddings; vector database ← Pinecone, Weaviate, pgvector, Qdrant,
  Milvus, Chroma; AI gateway.
- **Platform / infra:** internal developer platform ← IDP; platform engineering; Temporal; service mesh
  (confirm existing Kubernetes / Docker / Terraform / Kafka / RabbitMQ / event-driven / CI/CD / GitOps /
  gRPC / AWS / Azure / GCP rather than re-adding them).

## Done when

- Every target-stack term above is present as a canonical entry or a safe alias of one, and the audit
  notes in the PR which terms already existed (no duplicate canonical — the loader rejects duplicates).
- New aliases are whole-token-safe (no over-broad alias that would over-tag, e.g. `"ai"`); the existing
  `TechnologyVocabulary` construction validation still passes on load.
- A test asserts a description mentioning the target stack (MCP, Claude, RAG, LangGraph, pgvector,
  Temporal, platform engineering, …) tags to the expected canonical names and tags nothing spuriously.
- The vocabulary stays reviewable in a diff; the change is YAML-only plus the assertion test.

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-07 ·
`src/JobHunter.Infrastructure/Normalization/technology-vocabulary.yaml` · [[T07-technology-tagging]] ·
[[../data-model]] §job_technologies
