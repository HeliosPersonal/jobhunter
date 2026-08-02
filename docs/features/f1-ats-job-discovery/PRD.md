---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f1-ats-job-discovery, mvp, jobhunter]
---

# PRD — f1-ats-job-discovery

> **Inputs (required):** [[../../CONTEXT]] · [[../../00-overview/idea-brief|idea-brief]] §2, §7 · [[../../00-overview/sad|SAD]] §6.1
> **External context:** [[../../00-overview/adr/0009-ats-first-no-linkedin|ADR-0009]], [[../../ARCHITECTURE-OPEN-DECISIONS|O1]]

## 1. Context

The entire product is downstream of inventory. A ranking engine with nothing to rank is a demo, and
inventory quality caps everything: a company missing from the registry is invisible no matter how
good the model is, and a source that silently returns zero is indistinguishable from a quiet week.

The competitive premise ([[../../00-overview/idea-brief|brief]] §2) is that ATS boards carry a role
hours to days before aggregators do. Realising that advantage means fetching often enough to matter
(every six hours) while staying a good citizen of hosts we do not own — which is a rate-limiting and
quarantine problem, not a crawling problem.

Discovery is also where third-party fragility enters the system. Four providers, each free to change
their JSON without notice. The design consequence is that a source is an isolated failure domain: a
broken adapter degrades one provider, never a Run.

## 2. Goals

- Maintain a registry of target companies and know, for each, which ATS hosts its jobs and under
  which board identifier.
- Fetch every active company's board on a six-hourly cycle, storing each payload immutably with its
  fetch metadata.
- Never re-store unchanged content, so the volume downstream reflects genuine change.
- Degrade one source at a time, visibly, and recover automatically.
- Respect every host: robots directives, backoff signals, a declared identity and a per-host budget.

## 3. Non-goals

- Interpreting a posting into a canonical Job — F2 owns normalisation and deduplication.
- Any judgement about a job's quality — F3 and F4.
- LinkedIn, Indeed, Glassdoor or any source requiring browser automation
  ([[../../00-overview/adr/0009-ats-first-no-linkedin|ADR-0009]]).
- Detecting that a job has *changed* meaningfully — F1 only knows the bytes differ.
- Company enrichment (funding, size, stage) — F3 sets stage; F8 researches.

## 4. User stories

### US-01: Discover jobs from the companies I care about
**As the** Owner **I want** the platform to check every company on my list several times a day
**so that** a new opening reaches me while the applicant pool is still shallow.

### US-02: Add a company without knowing its ATS
**As the** Owner **I want** to add a company by its domain alone **so that** maintaining the
registry costs me nothing beyond a name.

### US-03: Keep working when a company changes ATS
**As the** Owner **I want** the platform to notice when a company moves to a different ATS
**so that** its jobs do not silently disappear from my digest.

### US-04: Not be blocked or blamed for rude crawling
**As the** Owner **I want** every fetch to respect the host's stated rules and rate limits
**so that** the platform is never blocked, and never something I would be embarrassed to publish.

### US-05: Know when a source stops working
**As the** operator **I want** a failing source to be isolated, reported and retried on its own
schedule **so that** one broken provider never costs me a day of inventory.

### US-06: Trust that nothing was lost
**As the** operator **I want** every fetch recorded and every payload retained verbatim
**so that** I can reprocess history when normalisation improves, without re-fetching.

## 5. Acceptance criteria

### AC-01 (US-01) — happy path
**Given** a set of active companies each with a confident ATS binding
**When** the six-hourly discovery cycle runs
**Then** each company's board is fetched exactly once in that cycle, and every posting returned is
recorded as raw material available to the next stage.

### AC-02 (US-01) — domain invariant
**Given** a posting whose content has not changed since it was last fetched
**When** it is fetched again
**Then** no new raw record is created, the existing record's last-seen time is updated, and no
downstream work is triggered.

### AC-03 (US-02) — happy path
**Given** a company identified only by its domain
**When** the platform attempts to determine where its jobs are published
**Then** it either establishes a binding with recorded evidence and a confidence level, or records
that no board was found — and never guesses without evidence.

### AC-04 (US-02) — error path
**Given** a company whose domain resolves to more than one plausible board
**When** detection runs
**Then** the ambiguity is recorded with all candidates and the company is not activated for
discovery until the ambiguity is resolved.

### AC-05 (US-03) — cross-context
**Given** a company whose existing binding stops returning results while a different provider begins
returning them
**When** binding re-detection runs
**Then** the previous binding is retired, the new one is recorded, and the company's previously
discovered jobs remain associated with the same company.

### AC-06 (US-04) — domain invariant
**Given** a host that publishes rules restricting automated access to a path
**When** discovery would fetch that path
**Then** it is not fetched, and the decision is recorded.

### AC-07 (US-04) — domain invariant
**Given** a host that signals it is being called too often
**When** the platform receives that signal
**Then** it waits at least as long as the host asked before contacting that host again, and it
never shortens the wait based on its own schedule.

### AC-08 (US-05) — error path
**Given** a source that fails on two consecutive attempts
**When** the next cycle would fetch it
**Then** the source is skipped, the operator is informed once, other sources are unaffected, and the
source is retried automatically after a cooling period.

### AC-09 (US-05) — cross-context
**Given** several sources are degraded
**When** the daily digest is produced
**Then** it states that inventory was collected from fewer sources than usual, rather than
presenting a reduced day as a normal one.

### AC-10 (US-06) — domain invariant
**Given** any raw payload that has been stored
**When** anything downstream processes it
**Then** the stored payload is unchanged and remains byte-identical to what the provider returned.

### AC-11 (US-06) — happy path
**Given** an attempt to fetch a source, successful or not
**When** the attempt completes
**Then** the outcome, the timing and the resulting status are recorded, so that the health of every
source over time is answerable from stored data alone.

### AC-12 (US-01) — authorization
**Given** a request to add, deactivate or force-refresh a company
**When** it arrives without operator credentials
**Then** it is refused and the registry is unchanged.

> **Cross-feature:** the registry-mutation endpoint that enforces the operator scope
> (`jobhunter:admin`) is implemented in **F9 T07**, which owns the API host; F1 defines the
> requirement and asserts it in its test plan (`RegistryMutation_WithoutOperatorScope_IsRefused`).
> The epic DoD "AC-01…AC-12 covered" is truthful via that seam, not an orphan.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Discovery cycle duration | < 20 min for 300 companies | `jobhunter.discovery.cycle_duration` |
| Per-host request rate | ≤ 1 req/s default, configurable per source | Redis token bucket, asserted by test |
| Fetch timeout | 30 s per request; 5 min per source | HttpClient policy |
| Response size cap | 10 MB; larger is rejected unread | Stream guard |
| Concurrency | ≤ 8 companies in flight | `Parallel.ForEachAsync` degree |
| Unchanged-content re-store rate | 0% — a byte-identical payload never creates a row | `uq_raw_postings_dedup` |
| Source availability | ≥ 95% of attempts succeed at steady state | `jobhunter.source.failures` ÷ attempts |
| Binding detection accuracy | ≥ 95% correct on a 50-company labelled set | Fixture suite |

## 6.1 Security / privacy

- **Data classification:** public — job postings and company metadata only.
- **Personal data touched:** none. Recruiter names present in a payload are stored as part of the
  immutable raw record but are never extracted, indexed or sent anywhere.
- **AuthZ/AuthN impact:** registry mutations require the operator scope (AC-12); discovery itself is
  triggered only by the internal scheduler.
- **Abuse cases:**
  - The platform is mistaken for a hostile crawler → declared identity, robots compliance, per-host
    budget, honoured backoff (AC-06, AC-07).
  - A hostile payload inflates storage or memory → 10 MB cap, streamed rejection.
  - A company-supplied URL points at internal infrastructure → fetch targets must resolve to public
    addresses; private and link-local ranges are refused ([[../../engineering/security]] §4).
  - Registry poisoning via an unauthenticated write → operator scope required (AC-12).
- **Security review:** N/A — public data, no credentials to third parties, no inbound surface beyond
  an authenticated admin endpoint.

## 7. Metrics / KPIs

- **Companies with a confident binding** — baseline 0, target ≥ 90% of the registry.
- **Postings ingested per day** — baseline 0, target ≥ 800 raw, ≥ 150 changed.
- **Source success rate** — baseline n/a, target ≥ 95% rolling 7 days.
- **Quarantined sources at steady state** — target 0; any sustained non-zero is a defect.
- **Detection accuracy** — target ≥ 95% on the labelled set, re-measured whenever an adapter changes.

## 8. Open questions

- [ ] Registry seeding: curated list, directory crawl, or both? — owner: Viacheslav — *default:
  both; ~300 curated companies committed as YAML, plus a weekly expansion crawl.* Blocks T03.
  ([[../../ARCHITECTURE-OPEN-DECISIONS|O1]])
- [ ] Raw retention window before pruning — owner: Viacheslav — *default: 90 days.* ([[../../ARCHITECTURE-OPEN-DECISIONS|O3]])
- [ ] Should Tier-2 sources (JSON-LD career pages) ship in F1 or be deferred? — owner: Viacheslav —
  *default: the port and one reference implementation ship in F1; breadth is post-M2.*

## DoD self-check

- [x] Coverage types present: happy (01, 03, 11), error (04, 08), authorization (12), domain invariant (02, 06, 07, 10), cross-context (05, 09)
- [x] No HTTP verbs, URLs, status codes, class names, JSON or SQL in §5
- [x] Every US has ≥1 AC; every AC names its US
- [x] NFRs measurable; every open question has an owner and a default
