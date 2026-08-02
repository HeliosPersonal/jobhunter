---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0009 — ATS-first ingestion; LinkedIn and aggregators out of scope

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The original plan already leans this way ("start with ATS systems instead of LinkedIn", "LinkedIn
scraping should be optional"). This ADR makes it a decision rather than a preference, because it
determines the entire ingestion architecture: whether `IJobSource` implementations are simple HTTP
JSON clients or a browser-automation and anti-bot-evasion subsystem.

## Decision drivers

- Greenhouse, Lever, Ashby and Workable expose unauthenticated, documented, stable JSON board
  endpoints. No scraping, no ToS grey zone, no anti-bot arms race.
- LinkedIn actively defends against automated access. Circumventing that is out of bounds
  ([[../../CONTEXT]] invariant 10) and would make the project unpublishable as a portfolio piece.
- ATS boards carry the job *earlier* than aggregators, which is the actual competitive advantage
  ([[../idea-brief]] §2).
- Aggregators duplicate ATS content, adding dedup load and staleness for no new inventory.

## Considered options

1. **ATS APIs only.**
2. **ATS APIs + headless-browser LinkedIn scraping.**
3. **ATS APIs + a paid aggregator API.**
4. **Aggregators only.**

## Decision outcome

**Chosen: Option 1**, with a defined expansion order.

**Tier 1 — structured ATS APIs** (MVP): Greenhouse, Lever, Ashby, Workable. One `IJobSource` adapter
each, plain `HttpClient` + `System.Text.Json`, contract-tested against recorded fixtures.

**Tier 2 — semi-structured** (post-MVP): SmartRecruiters, Recruitee, and company career pages that
publish `schema.org/JobPosting` JSON-LD. Same port, more forgiving parser, lower confidence score.

**Tier 3 — never**: LinkedIn, Indeed, Glassdoor listings, and anything requiring browser automation
or anti-bot circumvention. Revisit only if an official API with acceptable terms appears
([[../idea-brief]] §14 item 1).

Every fetch honours `robots.txt`, `Retry-After`, a per-host token bucket, and a descriptive
`User-Agent` identifying the project and a contact address. A source returning 403/429 twice
consecutively is quarantined for 24 h rather than retried harder.

## Consequences

**Positive**
- Ingestion is ordinary HTTP + JSON: fast, testable offline, and stable across months.
- No legal or ToS exposure; the repository can be public.
- Earlier inventory than aggregators, which is the actual product advantage.

**Negative**
- Coverage is bounded by the company registry. A company not in the registry is invisible.
  Mitigated by F1's ATS-directory expansion crawl and a curated seed list.
- No jobs from companies that publish only to LinkedIn. Accepted — those are also the companies
  least likely to fit the target profile.

**Neutral**
- The `IJobSource` port is agnostic; if an official aggregator API ever becomes worthwhile, it is a
  new adapter and nothing else changes.

## Links

- Brief: [[../idea-brief]] §14 item 1
- CONTEXT: [[../../CONTEXT]] §4, invariant 10
- Feature: [[../../features/f1-ats-job-discovery/index|F1]]
