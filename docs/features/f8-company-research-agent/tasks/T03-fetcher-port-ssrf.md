# T03 — IResearchFetcher port and SSRF-safe fetch path

**Layer:** scrapers · **Deps:** — · **Est:** L · **Owner:** Viacheslav

## What

The `IResearchFetcher` port and the guarded fetch path. This is where the feature's real
risk lives: targets derive partly from model output and from company-controlled pages, so every URL
passes a category allowlist **and** a public-address check — re-checked after every redirect, because
a redirect into private space is the classic bypass.

## Done when

- Every case in [[../test-plan|test-plan]] §The SSRF suite is refused, and each asserts the request was **not made**.
- A redirect from a public host into private space is refused after the redirect, not before it.
- DNS is resolved once and the connection is made to the resolved address, closing the rebinding window.
- Non-allowlisted hosts are refused per category, with the refusal logged.
- All fetching goes through F1's politeness handler — an architecture test forbids a new `HttpClient` here.
- The per-company budget of 12 requests and 60 s is enforced by the orchestrator, asserted with a counting handler.

## Links

[[../sad]] §10 QG-3 · [[../../../engineering/security]] §4 · [[../../f1-ats-job-discovery/tasks/T04-politeness-handler|F1 T04]]
