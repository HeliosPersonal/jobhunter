# T04 — PolitenessHandler: rate limit, robots, SSRF, user-agent

**Layer:** infra/http · **Deps:** — · **Est:** L · **Owner:** Viacheslav

## What

One `DelegatingHandler` on the shared `HttpClient` pipeline enforcing every outbound
rule: the declared `User-Agent`, a Redis token bucket per host, `robots.txt` compliance with a 24 h
cache, `Retry-After` honoured exactly, a 10 MB streamed response cap, and an SSRF guard rejecting
private and link-local addresses. Adapters get an `HttpClient` and cannot construct their own.

## Done when

- A request to a robots-disallowed path is not made, and the decision is recorded (AC-06).
- `Retry-After: 120` results in a wait of at least 120 s; the handler never shortens it (AC-07).
- The 61st request to one host inside a minute is deferred, not dropped.
- A response exceeding 10 MB is abandoned before full buffering.
- A host resolving to `10.0.0.0/8`, `127.0.0.0/8` or `169.254.0.0/16` is refused.
- Unreachable `robots.txt` is treated as allow; malformed `robots.txt` as disallow.
- An architecture test asserts no type in `JobHunter.Scrapers` constructs `HttpClient` (QG-2).

## Links

[[../sad]] §8 · [[../../../engineering/security]] §4
