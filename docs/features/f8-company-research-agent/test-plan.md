---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "15"
ticket: ""
tags: [sdlc/stage-15, feature/f8-company-research-agent, mvp, jobhunter]
---

# Test plan — f8-company-research-agent

> Two suites define this feature: the **uncited-claim suite** (QG-1) and the **SSRF suite** (QG-3).
> The second is why a security review is required before it ships.

## Levels

| Level | Scope | Network | Docker | Tooling |
|---|---|---|---|---|
| Unit | Citation verification, URL normalisation, freshness, target selection, budget enforcement | No | No | xUnit |
| Fixture | Each fetcher against recorded responses; content extraction | No | No | xUnit + `Fixtures/` |
| **Uncited claim** | Synthesis fixtures containing fabricated citations | No | No | xUnit |
| **SSRF** | Adversarial URLs through the full fetch path | No | No | xUnit + a stub resolver |
| Integration | Dossier persistence, the not-null source constraint, freshness refresh, stage feedback | No | Yes | Testcontainers |
| Messaging | Target selection from ranking; on-demand requests | No | Yes | Testcontainers |
| Contract | Live sources still match the consumed shape | **Yes** | No | Weekly, alert-only |

## AC coverage

| AC | Test | Level |
|---|---|---|
| AC-01 | `TopRankedCompany_GetsDossier_CoveringSupportedCategories` | Messaging |
| AC-02 | `ClaimWithoutMatchingSource_IsDiscarded_NeverStored` | **Uncited claim** |
| AC-03 | `EveryClaim_CarriesTheObservedDateOfItsSource` | Integration |
| AC-04 | `LayoffsAndFundingDifficulty_AreMarkedAsWarnings_AndSurfacedFirst` | Fixture + Integration |
| AC-05 | `OnDemandRequest_QueuesResearch_AndAcknowledges` | Messaging |
| AC-06 | `StaleDossier_IsRefreshed_NotPresented` | Integration |
| AC-07 | `CompanyWithFewSources_StillProducesDossier_RecordingUnavailableCategories` | Fixture |
| AC-08 | `FabricatedClaims_AreDiscardedAndCounted_SupportedOnesKept` | **Uncited claim** |
| AC-09 | `ResearchRequestOrRead_WithoutOwnerScope_IsRefused` | API |
| AC-10 | `FundingCategory_UpdatesCompanyStage` | Integration |

## The uncited-claim suite

Synthesis fixtures deliberately containing claims that cite URLs never fetched:

| Fixture | Assert |
|---|---|
| All claims cite real fetched URLs | All stored; discard count is 0 |
| One claim cites a plausible but never-fetched URL | That one discarded, the rest stored, count is 1 |
| A claim cites a real URL differing only by trailing slash | **Stored** — normalisation covers scheme, host case and trailing slash |
| A claim cites a real host with a different path | **Discarded** — a different document, not a formatting variant |
| A claim cites a URL from a different company's research | Discarded — the fetched set is scoped per dossier |
| Every claim is fabricated | All discarded; the dossier is stored with zero claims and the count recorded |
| A claim cites a real URL with an injected query parameter | Discarded — exact match after normalisation, no fuzzy tolerance |

Plus the structural assertion: **every row in `research_claims` has a non-null source that resolves to
a `research_sources` row for the same dossier.** Because the column is `NOT NULL` with a foreign key,
an uncited claim is unrepresentable rather than merely rejected — but the test asserts it anyway,
because a constraint nobody has tried to violate is a constraint nobody has verified.

## The SSRF suite

The reason this feature needs a security review. Adversarial targets pushed through the full fetch
path with a stub resolver:

| Target | Must be |
|---|---|
| Loopback address | refused |
| Private ranges `10.x` and `192.168.x` | refused |
| The cloud metadata address `169.254.169.254` | refused |
| IPv6 loopback and unique-local | refused |
| Decimal-encoded loopback | refused |
| Hex-encoded loopback | refused |
| A public host that **redirects** into private space | refused **after the redirect** — the classic bypass |
| A host resolving public then private on a second lookup | refused — resolve once, connect to the resolved address |
| A non-HTTP scheme | refused at the scheme check |
| A public host not on the category allowlist | refused |
| A public, allowlisted host | permitted |

Every case asserts the request was **not made**, not merely that the response was discarded.

## Edge cases / error paths

- A company with no website → most categories unavailable; the dossier still exists (AC-07).
- Every fetcher fails → all eight categories recorded unavailable and no synthesis is submitted.
- A fetched page of 5 MB → truncated at 20 000 characters at a paragraph boundary before synthesis.
- A page that is entirely JavaScript with no text → treated as no document; there is no headless browser.
- The GitHub organisation does not exist → category unavailable, not an error.
- The same company is top-ranked on consecutive days → the freshness check prevents a re-fetch (AC-06).
- News and layoffs at 8 days old → refreshed at the shorter 7-day threshold while the others are not.
- Synthesis would breach the cost ceiling → research is skipped for the day; the digest is unaffected.
- A claim over 300 characters → truncated at a sentence boundary, or discarded if that is impossible.
- The funding category disagrees with the recorded company stage → the newer observation wins, and the
  change is recorded.
- More than five candidates → the top five by score; the rest wait for a later cycle.

## Test data

- Recorded fixtures per fetcher under `Fixtures/research/<category>/`, including empty and malformed cases.
- Synthesis fixtures under `Fixtures/research/synthesis/`, covering all seven uncited-claim cases.
- A stub DNS resolver returning controlled addresses for the SSRF suite.
- `CompanyBuilder` with a configurable web presence.

## NFR validation

- At most 5 automatic dossiers per day → asserted in target selection.
- Cost under $0.05 per dossier → computed from the pricing table over the fixture corpus.
- Fetch budget of 12 requests and 60 s → asserted with a counting handler and a fake clock.
- **Uncited claims presented: 0** → the uncited-claim suite plus the structural assertion.
- Freshness thresholds → asserted at 29, 30 and 31 days, and at 6, 7 and 8 for news.
- Category coverage of at least 5 of 8 → asserted for three well-known-company fixtures.

## CI

- **PR:** unit, fixture, uncited-claim, SSRF, integration, messaging.
- **Weekly:** contract tests against live sources, alert-only.
- **Pre-ship:** security review, with the SSRF suite as its evidence ([[PRD]] §6.1).

## Related

[[../../engineering/testing-strategy]] · [[../../engineering/security]] §4 · [[sad]] §10
