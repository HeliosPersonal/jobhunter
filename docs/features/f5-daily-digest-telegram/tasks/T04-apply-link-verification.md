# T04 — Apply-link verification

**Layer:** app · **Deps:** T03 · **Est:** S · **Owner:** Viacheslav

## What

Verify each candidate card's apply destination before presenting it, with a 5 s timeout
and bounded parallelism. The verifier produces one of three outcomes: **reachable**,
**confirmed-unreachable** (a definitive 4xx/5xx or a DNS failure), or **unverified** (a timeout, or a
URL the politeness handler cannot fetch because `robots.txt` disallows it). Only a
**confirmed-unreachable** link drops a card; an **unverified** result keeps the card and flags it
"link unverified", because a slow host or a robots-blocked path is not a closed job. Timeout ≠
confirmed-unreachable.

## Done when

- A confirmed-unreachable destination — a definitive 4xx/5xx response or a DNS failure — excludes the card (AC-11); `apply_url_verified = false` and it is not presented as an actionable card.
- A **timeout** keeps the card, records verification as inconclusive, and renders it with the "link unverified" flag rather than dropping it.
- A **robots-disallowed** apply URL is unverifiable, not unreachable: the card is kept and shown with the same "link unverified" flag.
- Verification runs with bounded parallelism and never exceeds the assembly window.
- Requests go through the shared politeness handler, so verification is rate-limited like any other fetch and honours `robots.txt`.
- A job whose link is confirmed-unreachable is also flagged for the lifecycle sweep, so F2 can close it.

## Delivered

- **`ApplyLinkStatus`** (Domain/Reporting) — the three-valued verdict: `Reachable`,
  `ConfirmedUnreachable` (a definitive 4xx/5xx or a DNS/transport failure — drops the card, flags the
  job), `Unverified` (a timeout, a rate deferral, a `robots.txt` refusal, or a host that will not answer
  `HEAD` — keeps the card, "link unverified"). A timeout is `Unverified`, never `ConfirmedUnreachable`
  (D3): a slow host is not a closed job.
- **`IApplyLinkVerifier`** (Domain/Abstractions port) + **`ApplyLinkVerifier`** (Infrastructure/Http) —
  issues a `HEAD` through the shared politeness-gated client (`PoliteHttp.ClientName`), so the honest
  User-Agent, HTTPS-only rule, SSRF guard, `robots.txt` check and per-host rate budget all apply exactly
  as to any other fetch — verification cannot circumvent politeness because it owns no `HttpClient`
  (invariant 10, QG-2). The probe is bounded by a linked `CancellationTokenSource` set to the configured
  timeout: when *our* bound fires we return `Unverified`; when the *caller's* token fires (the assembly
  window aborting) we propagate, so a shutdown is never mistaken for a slow link. A dead host is a value,
  never a thrown fault — one bad link cannot take the digest down.
- **`ApplyVerificationOptions`** (Application/Reporting) — `Timeout` (default 5 s) and `MaxParallelism`
  (default 8), startup-validated via `.Validate().ValidateOnStart()`.
- **`DigestAssembler` wiring** — selects the cards first (the cap is on what is worth probing), then
  verifies the selected set with bounded parallelism in score order, so surviving ranks stay
  deterministic. A `ConfirmedUnreachable` link drops its card and its job id is collected; a card's
  `apply_url_verified` is true only when `Reachable`. Verification still selects **nothing about the
  Owner** — it reads apply URLs off the `jobs` join, not the CV.
- **`ApplyDestinationUnreachable`** (Contracts) — the flag the read path raises, idempotency-keyed on
  `(JobId, ConfirmedAt)`. The assembler only *flags* — it never closes a Job from the read path.
  **`UnreachableApplyLinkHandler`** (Application/Lifecycle) consumes it, calls `Job.Close` at the
  confirmed instant and emits `JobClosed` with reason `ApplyLinkUnreachable` (distinct from the stale
  sweep's), so closure stays in the layer that owns it (F2). A quarantined job refuses closure; an unknown
  job exits cleanly; a replay closes at the same instant and the inbox collapses the duplicate
  (invariant 8).
- **`DigestCandidate` / `DigestScopeQuery`** — carry the job's `apply_url` (the apply destination, not the
  Owner's data) so the assembler has a URL to probe.

18 verifier tests (`Infrastructure.Tests/Http/ApplyLinkVerifierTests`, real `PolitenessHandler` over a
stub transport, zero network), 6 assembler T04 tests and 4 lifecycle-handler tests
(`Application.Tests`, zero database); the CV-leakage sentinel scan stays green; solution builds with zero
warnings.

## Links

[[../PRD]] AC-11 · [[../sad]] §11 D3 · [[../../f1-ats-job-discovery/tasks/T04-politeness-handler|F1 T04]]
