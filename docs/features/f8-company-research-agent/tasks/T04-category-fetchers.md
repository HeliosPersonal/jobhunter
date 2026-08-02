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
