---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f8-company-research-agent, mvp, jobhunter]
---

# PRD — f8-company-research-agent

> **Inputs:** [[../../CONTEXT]] §1 (CompanyResearch), invariant 5 · [[../../00-overview/idea-brief|idea-brief]] §15
> **External context:** [[../../ARCHITECTURE-OPEN-DECISIONS|O4]] — **resolved** by
> [[adr/0001-fetch-then-synthesise|ADR-F8-0001]] (curated fetchers plus synthesis, never open web search).

## 1. Context

By M4 the Owner gets ten ranked opportunities every morning. For the top two or three, the same
half-hour of manual work follows every time: find the funding history, skim the engineering blog,
check the GitHub org, read the Glassdoor reviews with appropriate scepticism, search for recent news
and any layoffs, and try to work out what the interview process involves.

It is exactly the kind of work that is tedious, repetitive, and entirely composed of public
information — which makes it automatable. It is also the work most often skipped, and skipping it is
how people end up three interviews deep at a company with eighteen months of runway.

The design question is not whether to automate it but **how to keep it honest**. A dossier is only
useful if the Owner can trust it, and a language model asked "tell me about Stripe" will produce a
confident, well-structured, partly-fabricated answer. Hence [[../../CONTEXT]] invariant 5: every
claim carries a source URL, and an uncited claim is dropped rather than shown. That single rule
determines the architecture — fetch first, synthesise second, and never the other way round
([[adr/0001-fetch-then-synthesise|ADR-F8-0001]]).

## 2. Goals

- Produce a concise dossier for companies that appear near the top of the digest, without being asked.
- Cover funding and stage, engineering culture, open source, employee sentiment, recent news, layoffs,
  technology stack and interview process.
- Attach a source to every single claim.
- Refresh a dossier when it is stale, not on every mention.
- Surface it where the decision is made — in the digest and on request.

## 3. Non-goals

- Researching every company. Only those that reach the top of a digest or are asked for.
- Contact or recruiter information. Not a CRM, and not a scraping target.
- Salary benchmarking — a separate post-MVP item in [[../../BACKLOG]].
- Interview question banks or preparation material — post-MVP.
- Any judgement about whether the Owner should apply. The dossier informs; it does not recommend.
- Paid data providers. Public sources only.

## 4. User stories

### US-01: Know who I would be joining
**As the** Owner **I want** a short profile of a company that ranks highly **so that** I can decide
whether to spend time on it.

### US-02: Trust what the profile says
**As the** Owner **I want** every statement to be traceable to a source **so that** I can verify
anything that would change my decision.

### US-03: Know if there are warning signs
**As the** Owner **I want** recent layoffs, funding difficulties or notable news surfaced
**so that** I am not blindsided.

### US-04: Understand the engineering culture
**As the** Owner **I want** their blog, open source and stated stack **so that** I can judge whether
the work would interest me.

### US-05: Know what the process involves
**As the** Owner **I want** what is publicly known about their interview process **so that** I can
judge the time commitment before starting.

### US-06: Not read stale information
**As the** Owner **I want** each statement dated **so that** I know how current it is.

### US-07: Ask about a company on demand
**As the** Owner **I want** to request research for any company **so that** I am not limited to what
ranked highly.

## 5. Acceptance criteria

### AC-01 (US-01) — happy path
**Given** a company appearing near the top of a daily digest
**When** the day's processing completes
**Then** a dossier exists for that company covering the supported categories, or records which
categories no information could be found for.

### AC-02 (US-02) — domain invariant
**Given** any statement within a dossier
**When** it is stored
**Then** it carries the address of the source it came from; a statement without one is discarded and
never presented.

### AC-03 (US-06) — domain invariant
**Given** any statement within a dossier
**When** it is presented
**Then** the date the information was observed is shown alongside it.

### AC-04 (US-03) — happy path
**Given** publicly reported layoffs or funding difficulty for a company
**When** its dossier is produced
**Then** these appear prominently rather than being buried among other categories.

### AC-05 (US-07) — happy path
**Given** the Owner requests research for a named company
**When** the request is processed
**Then** a dossier is produced or refreshed, and the Owner is told when it is ready.

### AC-06 (US-06) — cross-context
**Given** a dossier older than the freshness threshold
**When** the company appears near the top of a digest again
**Then** it is refreshed rather than the stale version being presented.

### AC-07 (US-01) — error path
**Given** a company for which most sources return nothing
**When** research runs
**Then** a dossier is still produced from what was found, and the categories with no information are
recorded as such rather than left ambiguous.

### AC-08 (US-02) — error path
**Given** a synthesis that produces statements not supported by any fetched source
**When** the result is processed
**Then** those statements are discarded, the discard is recorded, and the remaining supported
statements are kept.

### AC-09 (US-07) — authorization
**Given** a request to produce or read research
**When** it arrives from anyone other than the Owner
**Then** it is refused.

### AC-10 (US-04) — cross-context
**Given** a dossier that determined a company's funding stage
**When** it is stored
**Then** the company's recorded stage is updated, so ranking benefits from the better information.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Companies researched per day | ≤ 5 automatic, plus on-demand | Configuration, asserted |
| Cost per dossier | < $0.05 | Cost ledger, `stage=Research` |
| Fetch budget per company | ≤ 12 requests, ≤ 60 s total | Fetcher configuration |
| **Uncited claims presented** | **0** | Assertion on every claim row |
| Freshness threshold | 30 days | Configuration |
| Category coverage | ≥ 5 of 8 categories for a well-known company | Fixture corpus |
| On-demand latency | dossier ready within one daily cycle | Integration test |

## 6.1 Security / privacy

- **Data classification:** public — all sources are public web pages and APIs.
- **Personal data touched:** none. Individual employees' names appearing in a source are not extracted.
- **AuthZ/AuthN impact:** requesting and reading research is owner-scoped (AC-09).
- **Abuse cases:**
  - **Server-side request forgery** — this is the feature's real risk. Fetch targets derive partly from
    model output and from company websites, so every target must resolve to a public address and match
    an allowlisted host pattern ([[../../engineering/security]] §4).
  - Fabricated claims presented as fact → the citation guarantee (AC-02) plus the uncited-claim suite.
  - Prompt injection from a fetched page attempting to insert a claim → the synthesiser can only cite
    sources it was given, and an unmatched citation is discarded (AC-08).
  - Fetch volume becoming abusive to a third party → per-host budget through the shared politeness
    handler, exactly as F1.
- **Security review:** **required** — this is the only feature that fetches URLs influenced by model
  output, which makes SSRF a live concern rather than a theoretical one.

## 7. Metrics / KPIs

- **Dossiers produced per week** — informational.
- **Uncited claims discarded** — reported. A rising rate means the prompt is drifting toward assertion.
- **Category coverage** — target ≥ 5 of 8 for well-known companies; lower for small ones is expected
  and correct.
- **Owner-reported inaccuracies** — target zero. Any occurrence adds a case to the corpus.

## 8. Open questions

- [ ] Which review source, given the terms of the obvious ones? — owner: Viacheslav — *default:
  only sources with a usable public API or feed; skip the category rather than scrape.*
  ([[../../ARCHITECTURE-OPEN-DECISIONS|O4]])
- [ ] Should research run for saved companies as well as top-ranked ones? — owner: Viacheslav —
  *default: yes, once, on the first save.*
- [ ] Retention for dossiers — owner: Viacheslav — *default: keep indefinitely, refresh on staleness;
  the history of a company's trajectory has value.*

## DoD self-check

- [x] Coverage types: happy (01, 04, 05), error (07, 08), authorization (09), domain invariant (02, 03), cross-context (06, 10)
- [x] No implementation tokens in §5
- [x] Every US has ≥1 AC; NFRs measurable
