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

## Implementation

Three commits on top of F1's politeness pipeline, reusing its `SsrfGuard`, `PolitenessHandler` and the
named-client pattern rather than duplicating any of it.

**C1 — the port and its value.** `IResearchFetcher` (`Domain/Abstractions`) with `FetchedDocument`
(`Domain/Research`): one implementation per category, returning documents as values and never throwing —
an unavailable, refused or empty source is an empty list (AC-07).

**C2 — the category host allowlist.** `ResearchHostAllowlist` (`Infrastructure/Http`, internal): a
company-scoped category (engineering blog, stack, interview process) permits only the company's own
registrable domain and its subdomains; a third-party category permits a fixed host set (open source →
`github.com`). Matching is on a dot boundary, so `stripe.com.evil.test` and `notgithub.com` never match; a
category with no configured hosts is refused by default (a deny, never an allow-all).

**C3 — the guarded fetch path (the security core).** Two collaborating pieces, each unit-tested with zero
network:

- `IGuardedResearchFetch` (`Application/Abstractions`, → `GuardedResearchFetch` in `Infrastructure/Http`).
  For the initial request and *every* redirect it applies, in order: the HTTPS-only scheme check, the
  category allowlist, and — through F1's `PolitenessHandler` — the public-address check. The research client
  keeps `AllowAutoRedirect = false`, so this class follows redirects itself and re-validates each hop before
  it is fetched: a redirect from a public host into private space is refused *after* the redirect, which is
  the classic bypass. Every outcome is a value (`ResearchFetchResult`): refusal (scheme, allowlist, SSRF,
  robots), rate deferral, non-success status and a bounded redirect loop.
- `ResearchConnector` (`Infrastructure/Http`, internal): installed as the research client's socket
  `ConnectCallback`. It resolves the host **once**, refuses unless every resolved address is public, and
  dials the exact address it validated — closing the DNS-rebinding window where a name public at validation
  turns private at connect time. An IP literal is classified directly with no resolution.

Wired in `AddResearchHttp` (DI): the `PoliteHttp.ResearchClientName` named client is the politeness handler
plus the connector, and `IGuardedResearchFetch` is scoped. The QG-2 architecture rule is respected — the
fetch path lives in Infrastructure and Scrapers constructs no `HttpClient`.

**Tests.** `GuardedResearchFetchTests` is the SSRF suite: every adversarial target (loopback, private
`10.x`/`192.168.x`, metadata `169.254.169.254`, IPv6 loopback/ULA, decimal- and hex-encoded loopback —
which `Uri` normalises to `127.0.0.1` and the allowlist then refuses, a non-HTTPS scheme, a redirect into
private space, a redirect to a non-allowlisted host) asserts the request was **not made** via a recording
transport, and the permitted public allowlisted host is fetched. `ResearchConnectorTests` asserts the
resolve-once / pin-to-validated-address guarantee, each refusal case asserting no dial was made.

**Deferred to the orchestrator (T08).** The per-company budget of ≤12 requests and ≤60 s is enforced by the
orchestrator, not the fetcher — per this task's done-when wording — so it is asserted with a counting handler
and a fake clock there, not here.

## Links

[[../sad]] §10 QG-3 · [[../../../engineering/security]] §4 · [[../../f1-ats-job-discovery/tasks/T04-politeness-handler|F1 T04]]
