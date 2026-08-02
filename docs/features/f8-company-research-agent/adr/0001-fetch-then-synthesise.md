---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f8-company-research-agent, jobhunter]
---

# F8-0001 — Curated fetchers plus synthesis, never open web search

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The dossier must cover funding, engineering culture, open source, reviews, news, layoffs, stack and
interview process. There are three plausible ways to get there, and they differ mainly in how much
they can be trusted. Resolves [[../../../ARCHITECTURE-OPEN-DECISIONS|O4]].

The constraint that decides it is [[../../../CONTEXT]] invariant 5: every claim carries a source URL,
and an uncited claim is dropped rather than shown. That is not a stylistic preference. A dossier of
plausible unsourced statements is *worse* than no dossier, because the Owner might make a decision on
it — and a language model asked "tell me about Stripe" will produce fluent, well-organised, partly
fabricated prose with complete confidence.

## Decision drivers

- Invariant 5 requires that a claim's source be checkable, which means the source must be something we
  hold, not something the model asserts.
- The dossier informs a decision about spending several days of the Owner's time. Its failure mode is
  quiet and expensive.
- Verification must be mechanical. "Does this claim seem sourced" is a judgement; "is this URL in the
  set we fetched" is a set-membership check.
- The feature must be testable offline, which means the model's inputs must be fixtures we control.

## Considered options

1. **Model-only** — ask Claude what it knows about the company.
2. **Model with web search** — let the provider's search tool retrieve and cite.
3. **Curated fetchers, then synthesis over only what was fetched**, with citations verified against
   the fetched set.
4. **Paid data provider** — a company-data API.

## Decision outcome

**Chosen: Option 3.**

The order is the decision. One fetcher per category retrieves documents through F1's politeness
handler; **every document is stored with its exact URL and fetch time before the model runs**; the
synthesis prompt contains only that text and is told in the system prompt that anything not in the
documents does not exist; and every returned claim is checked against the fetched URL set. Unmatched
claims are **discarded and counted**, not flagged.

Discarding rather than flagging matters more than it looks. A claim marked "unverified" still gets
read as a claim — the Owner sees a sentence about the company and a caveat, and the sentence is what
sticks. Removing it entirely is the only presentation that cannot mislead.

Storing sources before synthesis is what converts the hard question ("did the model make this up?")
into a trivial one ("is this URL in the set?"). That inversion is the whole architecture.

Option 1 is unacceptable under invariant 5 and would be wrong about small companies in particular —
exactly the ones where the Owner has least independent knowledge. Option 2 is closer but the citations
come from the provider's retrieval rather than from documents we hold, so we cannot verify them, we
cannot fixture them, and the whole feature becomes untestable offline. Option 4 costs money for
coverage this product does not compete on, and would still need the citation discipline for everything
outside its schema.

## Consequences

**Positive**
- Every claim is verifiable by clicking a link. The dossier is trustworthy in the only sense that matters.
- Verification is mechanical and therefore reliable, rather than a heuristic that degrades quietly.
- Fully testable offline with recorded fixtures, including deliberately fabricated citations.
- A sparse dossier for an obscure company is *correct output*, not a failure — and the categories with
  nothing found are recorded explicitly.

**Negative**
- Coverage is bounded by the fetchers. A company that publishes nothing gets a thin dossier. This is
  the honest outcome and the design says so out loud.
- Eight fetchers to build and keep working against changing public sources. Mitigated by one port and
  isolated failure per category.
- **Fetch targets derive partly from model output and from company-controlled pages, which makes SSRF
  a live risk in this feature specifically.** Mitigated by a host allowlist plus public-address
  resolution checked again after redirects, and by a dedicated SSRF suite — and it is why this feature
  requires a security review before shipping.

**Neutral**
- The residual risk that a model asserts a plausible claim citing a URL that *was* fetched but does not
  support it is acknowledged rather than solved (SAD §11 D4). A sampling check in the corpus reduces
  it; nothing eliminates it short of extractive-only summarisation, which would lose too much value.

## Links

- [[../../../CONTEXT]] invariant 5 · [[../PRD]] AC-02, AC-08 · [[../sad]] §10 QG-1, QG-2, QG-3
- [[../contracts/research-schema]] §Citation verification · [[../../../ARCHITECTURE-OPEN-DECISIONS|O4]]
- [[../../../engineering/security]] §4
