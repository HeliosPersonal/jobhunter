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

## Implementation

Three types in `src/JobHunter.Claude/Prompts/`, following the same shape as the enrichment and
digest-narrative prompts so they share the snapshot-and-version discipline.

- **`ResearchSynthesisPrompt`** — `PromptVersion = "research-v1"`. The constant system prompt states the
  rule the feature rests on verbatim: *"You are a summariser, not an expert … If the documents do not
  say it, it does not exist,"* every claim citing a `sourceUrl` *"copied verbatim"*, thin documents →
  few claims, and `isWarning` for layoffs/down rounds. `RenderUser(ResearchPromptInput)` is pure — no
  clock, no state — so it is snapshot-tested against
  `Fixtures/research-prompt/user-content.snapshot.txt`. It numbers each document `[n]` with its
  `sourceUrl`, `category`, `observed` date and `title` followed by the extracted text, caps each
  document's text at `MaxDocumentChars = 20 000`, and lists the empty categories explicitly
  (`Categories with no documents found: …`, or `none`) — the single most effective guard against the
  model filling a known gap from memory. A test renders a famous company from one thin document and
  asserts none of its well-known facts leak in, so a sparse input can only ever produce a sparse
  dossier (QG-2).
- **`ResearchPromptInput` / `CategorisedDocument`** — the pure projection the render consumes: the
  display name, canonical domain, the fetched documents each tagged with the `ResearchCategory` whose
  fetcher found it (the category is a property of the fetch, not of `FetchedDocument`), and the list of
  categories that yielded nothing. It carries nothing about the Owner — the CV still crosses exactly
  one boundary, and it is F4's.
- **`ResearchSynthesisSchema` / `ResearchOutput` / `ClaimDto`** — the tool-use schema binds
  `record_research` to a bounded `summary` (`maxLength 500`) and a bounded `claims` array
  (`maxItems 20`), each item requiring `category` (an `enum` generated from `ResearchCategory`, so it
  cannot drift), `claim` (`maxLength 300`), `sourceUrl` (`format: uri`) and `isWarning`. Requiring the
  URL to be *present* encodes invariant 5 at the schema level; requiring it to be *true* is the
  verifier's job (T07), because a model can always cite a plausible URL it invented. `ResearchOutput`
  and `ClaimDto` are the parsed, **not-yet-verified** DTOs.

**Cost.** One dossier is one deep-tier item priced with `MaxOutputTokens = 900`. A cost test renders a
full four-document dossier (~15 000 input tokens after the per-document cap) and asserts, against the
pricing table rather than a magic number, that the estimate stays under the $0.05 ceiling (~$0.03 in
practice); a sparse document set is asserted to price far below a full one. Ledgering and the
pre-submission ceiling check are F3's existing machinery, wired by the orchestrator in T08 — over
budget skips research without touching the digest.
