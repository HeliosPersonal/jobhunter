# T04 — Category fetchers

**Layer:** scrapers · **Deps:** T03 · **Est:** L · **Owner:** Viacheslav

## What

Seven fetchers behind the port: engineering blog, open source, funding, news, layoffs,
stack and interview process. Each isolated, so a dead source degrades one category rather than the
dossier. Content is extracted to plain text at the boundary and capped.

## Done when

- Each fetcher passes its recorded fixtures, including empty and malformed responses.
- A failing fetcher records its category as unavailable and does not affect the others (AC-07).
- Extraction strips scripts and styles and caps at 20 000 characters at a paragraph boundary.
- A page with no extractable text is treated as no document — there is no headless browser.
- Every fetched document is stored with its exact URL and fetch time **before** synthesis (SAD §4 S2).
- Reviews are fetched only from sources with a usable public API or feed; otherwise the category is skipped.

## Links

[[../contracts/research-schema|contract]] §Fetcher set

## Implementation

Eight `IResearchFetcher` implementations in `src/JobHunter.Scrapers/Research/`, one per
`ResearchCategory`, all registered in `AddJobHunterScrapers` so the orchestrator (T08) dispatches over
a complete set without switching on the enum. All fetching goes through `IGuardedResearchFetch`
(Infrastructure, T03) — no fetcher constructs an `HttpClient` (QG-2). Each returns an empty list rather
than throwing, so a dead source degrades one category, never the dossier (AC-07).

- **Content extraction** (`Parsing/ResearchContentExtractor.cs`): a research-specific HTML→plain-text
  pass, stricter than the ATS `HtmlText` — it discards `<script>`/`<style>` bodies and comments, turns
  block tags into paragraph breaks and inline tags into spaces, decodes entities, and caps at
  20 000 chars on a paragraph boundary (word boundary for a single oversized paragraph). A single
  char-scan, no regex. A page yielding no extractable text is *no document* — there is no headless
  browser. `Parsing/PageTitle.cs` reads the first `<title>` for the document title.
- **Company-scoped fetchers** (`CompanyPageFetcher` base): `EngineeringBlogFetcher`,
  `StackFetcher`, `InterviewProcessFetcher` probe a small fixed path set on the company domain
  (`/blog`, `/engineering`, `/careers`, …). Each probed URL goes through the guard; a candidate that
  throws or is refused is isolated so the others still produce. A produced `FetchedDocument` carries
  the guard's `FinalUrl`, the page title, the extracted text and `IClock.UtcNow` as `ObservedAt` — the
  exact URL and fetch time are captured at the boundary, before synthesis (SAD §4 S2).
- **GitHub org fetcher** (`GitHubOrgFetcher`, category `OpenSource`): derives the org login from the
  domain's leading label and queries `api.github.com/orgs/{org}/repos` (allowlisted as a `github.com`
  subdomain). Summarises non-fork repos to one line each; a malformed body or empty summary yields no
  document.
- **Unconfigured feed categories** (`UnconfiguredSourceFetcher` base): `FundingFetcher`,
  `NewsFeedFetcher`, `LayoffsFetcher`, `ReviewsFetcher`. No public, auth-free, allowlistable feed host
  is configured for these yet, so each honestly logs the category as unavailable and returns no
  documents rather than scraping an un-vetted source — consistent with "reviews are fetched only from
  a source with a usable public API or feed; otherwise the category is skipped" and invariant 10.
  Choosing and allowlisting specific feed hosts is a deferred follow-up.

Tested with zero network via `FakeGuardedResearchFetch` (routes URLs to canned bodies, can simulate a
dead candidate) and `FakeClock`: path probing, document shape, no-extractable-text, refused, isolated
dead candidate, GitHub query/summary/malformed-body, and the four unconfigured categories reporting
their category and returning empty.
