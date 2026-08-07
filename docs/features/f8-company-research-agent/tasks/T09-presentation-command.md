# T09 — Presentation and on-demand command

**Layer:** telegram/api · **Deps:** T08 · **Est:** M · **Owner:** Viacheslav

## What

Render the dossier in the digest card layout — warnings first, then categories, every
claim with its date and a link — plus the `/company` command and two owner-scoped endpoints.

## Done when

- Every rendered claim shows its observed date and links to its source (AC-02, AC-03).
- Warnings appear before other categories.
- Unavailable categories are stated, so absence is visible rather than ambiguous.
- `/company` returns a fresh dossier, or queues research and acknowledges (AC-05).
- An unknown company offers to add it to the registry rather than failing.
- Both endpoints are owner-scoped; without the scope they are refused (AC-09).
- All dynamic text is escaped through F5's escaper.

## Implementation

Presentation splits into a pure formatting core (C1) and the two edges that carry it to the Owner — the
Telegram `/company` command (C2) and the owner-scoped research API (C3) — with the read side and the
on-demand queue behind ports so neither edge touches SQL.

- **Formatting core (C1).** `DossierView` is the presentation projection of a `ResearchDossierSnapshot`;
  `DossierFormatter` renders it in the digest card layout — warnings first, then the remaining categories,
  every claim carrying its observed date and a link to its source, and every unavailable category stated so
  absence is visible rather than ambiguous (AC-02/03/04, AC-07). All dynamic text is escaped through F5's
  MarkdownV2 escaper, so a hostile claim or company name cannot break the send.
- **Read and write ports (C2).** `ICompanyResearchQuery` resolves a company by the name the Owner typed and
  returns its latest dossier, keeping "no such company" (a null lookup — offer to add it) distinct from
  "known but never researched". `IResearchRequestWriter` queues a company for the next cycle. Both are read
  models / small writes defined in Domain; the `CompanyResearchQuery` Dapper impl joins `research_claims` to
  `research_sources` for each claim's URL, and the `ResearchRequestWriter` enqueues with `ON CONFLICT
  (company_id) WHERE NOT consumed DO NOTHING` against the partial unique index `uq_research_requests_open`,
  so a repeat request before the cycle drains the queue is an idempotent no-op (AC-05). A new
  `research_requests` table and the `ResearchRequest` entity back the queue. The `/company` handler resolves
  the name, presents a fresh dossier with its age, or queues research and acknowledges "ready with
  tomorrow's digest"; an unknown company offers to add it rather than failing.
- **Owner-scoped API (C3).** Two sub-resources on the company, both keyed by canonical domain:
  `GET /api/companies/{domain}/research` returns the latest dossier (`jobhunter:read`), 404 when the company
  is known but never researched — distinct from an unknown company — and never fabricates an absent dossier
  (invariant 5); `POST /api/companies/{domain}/research` queues the company for the next cycle
  (`jobhunter:admin`, a mutation) and returns 202, dialling no research inline. Requesting or reading
  research as anyone other than the Owner is refused — a read token on the write route is a 403 (AC-09). The
  `CompanyResearchResponse`/`CompanyClaimResponse` DTOs are hand-written and CV-free: category, claim,
  observed date (Unix seconds), source URL and the warning flag, warnings first.

## Links

[[../../f5-daily-digest-telegram/contracts/telegram-messages|F5 message contract]]
