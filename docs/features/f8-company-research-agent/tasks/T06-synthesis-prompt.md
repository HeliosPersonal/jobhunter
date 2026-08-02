# T06 — Synthesis prompt and schema

**Layer:** claude · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

`ResearchSynthesisPrompt` and its schema, submitted as one deep-tier item per company
through F3's machinery. The system prompt states the rule plainly: anything not in the supplied
documents does not exist. Empty categories are listed explicitly, which measurably reduces the
temptation to fill them from memory.

## Done when

- The prompt contains only fetched text — a test asserts no company knowledge is injected from elsewhere.
- Categories with no documents are listed explicitly in the prompt.
- The prompt rendering is snapshot-tested so a change is visible in a diff.
- Submission is ledgered and ceiling-checked like every other batch; over budget skips research without affecting the digest.
- Cost stays under $0.05 per dossier, asserted against the pricing table.
- A sparse document set produces a sparse dossier, not a rich one — asserted with a famous-company fixture (QG-2).

## Links

[[../contracts/research-schema|contract]] · [[../../f3-claude-batch-enrichment/tasks/T10-enrichment-submit|F3 T10]]
