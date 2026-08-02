---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f3-claude-batch-enrichment, mvp, jobhunter]
---

# PRD — f3-claude-batch-enrichment

> **Inputs:** [[../../CONTEXT]] §1–3 · [[../../00-overview/idea-brief|idea-brief]] §4, §7 · [[../../00-overview/sad|SAD]] §6.2
> **External context:** [[../../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]], [[../../00-overview/adr/0006-structured-output-contract|ADR-0006]], [[../../DECISION-LOG|D4]]

## 1. Context

A job posting is prose. Ranking needs facts: is this actually remote for someone in EMEA, will they
take a contractor, what does it really pay, how much AI work is involved, is the company two people
or two thousand. Four of those are decidable from the text by a model that reads carefully, and none
of them are decidable by a regex.

Doing that reading for 150 postings a day, every day, is only affordable in batch — which is the
constraint that shapes this entire feature. The Batch API is asynchronous over hours: you submit, you
poll, you retrieve, and in between the process may restart, the pod may be rescheduled, and the node
may reboot. **The interesting engineering is not the prompt; it is surviving the wait.**

That is why F3 introduces the `Run`. A Run is a durable state machine with a cost ledger, owning one
day's intelligence work end to end. It is the answer to "what happens if the worker dies at 03:47",
and it is reused unchanged by F4 (matching), F5 (digest synthesis) and F8 (research). Building it
properly once is the whole point of doing F3 before F4.

The second thing F3 must get right is money. An LLM pipeline with a budget alert is a pipeline that
tells you *after* it overspent. [[../../CONTEXT]] invariant 6 makes the ceiling a correctness
property, checked before submission, which is why it is testable and why a breach aborts rather than
truncates.

## 2. Goals

- Produce, once per day, a structured assessment of every newly discovered job: pay, remoteness,
  contractor friendliness, timezone fit, AI intensity, technologies, company stage and the reasons
  behind each.
- Complete a day's work at a cost known before it is incurred, and never exceed the agreed ceiling.
- Survive interruption at any point without repeating paid work or losing completed work.
- Isolate a single unparseable result so that it costs one job, not one day.
- Provide the Run and Batch machinery that F4, F5 and F8 reuse without extending it.

## 3. Non-goals

- Comparing a job against the CV — F4 owns matching. F3's assessment is about the *job*, independent
  of who is reading it.
- Ranking, ordering or filtering — F4 and F7.
- Researching the company beyond what the posting itself says — F8.
- Any synchronous model call. Everything goes through batch ([[../../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]]).
- Prompt experimentation tooling. Prompts are versioned code; an eval harness is in [[../../BACKLOG]].

## 4. User stories

### US-01: Know what a job actually offers
**As the** Owner **I want** every new job assessed for pay, remoteness, contractor friendliness and
timezone **so that** I can rule out a mismatch without reading the posting.

### US-02: Know why an assessment says what it says
**As the** Owner **I want** every assessment to carry its reasons **so that** I can tell a confident
judgement from a guess.

### US-03: Never be surprised by the bill
**As the** Owner **I want** a hard daily spending limit that is checked before money is spent
**so that** a bug cannot cost me a month's budget overnight.

### US-04: Lose nothing to a restart
**As the** operator **I want** a day's work to survive any interruption **so that** a deploy, a
reboot or a crash never costs a digest or a duplicate charge.

### US-05: Not lose a day to one bad result
**As the** operator **I want** an unparseable result isolated **so that** one malformed item does not
fail the other hundred and forty-nine.

### US-06: Know what a day cost
**As the** operator **I want** spending recorded per stage and per model tier **so that** a cost
increase is attributable rather than mysterious.

### US-07: Attribute a quality change to a prompt change
**As the** operator **I want** every assessment to record which prompt version produced it
**so that** a drop in quality can be traced to the change that caused it.

## 5. Acceptance criteria

### AC-01 (US-01) — happy path
**Given** a set of jobs discovered since the previous day's cut-off
**When** the daily assessment completes
**Then** every one of those jobs has exactly one assessment for that day, carrying pay estimate,
remote and contractor indications, timezone band, AI-usage level, technologies and company stage.

### AC-02 (US-02) — domain invariant
**Given** an assessment produced for a job
**When** it is stored
**Then** it carries at least one reason; an assessment with no reasons is rejected and recorded as
failed rather than persisted.

### AC-03 (US-03) — domain invariant
**Given** a daily spending ceiling and an estimate of what the next portion of work will cost
**When** the estimate would take the day past its ceiling
**Then** that work is not submitted, no money is spent, the day is marked as having stopped for cost,
and the operator is told.

### AC-04 (US-03) — domain invariant
**Given** a day's work has begun
**When** any portion of work is submitted
**Then** its estimated cost has already been recorded against the day's ledger, so the ledger is
never behind reality.

### AC-05 (US-04) — cross-context
**Given** work has been submitted to the model provider and is still in progress
**When** the process is interrupted and restarts
**Then** the same day's work resumes, the already-submitted work is polled rather than re-submitted,
and no additional cost is incurred for it.

### AC-06 (US-04) — cross-context
**Given** results have been retrieved and partially stored
**When** the process is interrupted and restarts
**Then** re-processing the same results produces no duplicate assessments and no additional charge.

### AC-07 (US-05) — error path
**Given** a batch of results in which some items do not conform to the expected shape
**When** the results are processed
**Then** the conforming items are stored, each non-conforming item is recorded with what was wrong
and its original content retained, and the day continues.

### AC-08 (US-05) — error path
**Given** an item that failed to produce a usable assessment
**When** the next day's work runs
**Then** that job is retried once at the cheaper tier, and if it fails again it is not retried
indefinitely.

### AC-09 (US-04) — error path
**Given** submitted work that the provider never completes
**When** the deadline for that day's delivery arrives
**Then** the day proceeds with whatever completed, the incomplete portion is carried to the next day,
and the delivery is not delayed.

### AC-10 (US-06) — happy path
**Given** a completed day of work
**When** its cost is inspected
**Then** the amount spent is attributable to each stage and each model tier, and the total matches
the sum of its parts.

### AC-11 (US-07) — domain invariant
**Given** any stored assessment
**When** it is inspected
**Then** it records which version of the instructions produced it.

### AC-12 (US-01) — authorization
**Given** a request to start, resume or abort a day's work
**When** it arrives without operator credentials
**Then** it is refused and no work is started.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Cost per Run (enrichment stage) | < $0.50 at 150 jobs | `cost_ledger_entries` sum |
| Cost ceiling default | $2.00 per Run, configurable | `runs.ceiling_usd` |
| Estimate accuracy | within 20% of actual | Estimate vs ledger, tracked per Run |
| Cheap-tier token share | ≥ 70% of enrichment-stage tokens | Ledger by tier |
| Batch turnaround | ≤ 4 h p95 submit → retrieved | `jobhunter.batch.latency` |
| Poll interval | 2 min, exponential backoff to 15 min, 6 h cap | Configuration, asserted |
| Parse success rate | ≥ 97% of items per batch | `jobhunter.llm.parse_failures` |
| Resume correctness | 0 duplicate charges, 0 duplicate rows after any interruption | Crash-matrix test |
| Run wall clock | ≤ 5 h from start to enrichment complete | `jobhunter.run.duration` |

## 6.1 Security / privacy

- **Data classification:** job descriptions are public; the assessment is internal.
- **Personal data touched:** none. **F3 never sends the CV** — that boundary belongs to F4 alone,
  and keeping it out of F3 is what makes the CV's single crossing point auditable.
- **AuthZ/AuthN impact:** Run control is operator-scoped (AC-12); the pipeline itself is triggered
  only by the internal scheduler.
- **Abuse cases:**
  - Prompt injection via a job description → output is schema-bound, the model has no tools, and the
    result is only ever written to typed columns. Worst case is one wrong assessment, which its
    reasons will expose ([[../../engineering/security]] §5).
  - Runaway spend from a retry loop → pre-submission ceiling (AC-03) plus the once-only retry (AC-08).
  - API key exposure → Infisical at runtime; the key never appears in a log, a span or an image layer.
  - Provider outage used as a denial vector → partial-day policy (AC-09) means delivery is never blocked.
- **Security review:** N/A — no personal data crosses a boundary in this feature.

## 7. Metrics / KPIs

- **Enrichment coverage** — target 100% of new jobs assessed within one day.
- **Cost per assessed job** — baseline unknown, target < $0.001.
- **Parse success rate** — target ≥ 97%; below 95% is an alert ([[../../operations/runbooks|R5]]).
- **Estimate accuracy** — target within 20%; systematic drift means the pricing table is stale.
- **Resume events with zero duplicate cost** — target 100%.

## 8. Open questions

- [ ] Should enrichment run on cheap tier only, or escalate ambiguous items to the deep tier?
  — owner: Viacheslav — *default: cheap only for MVP; escalation is measurable later by comparing
  a sample against deep-tier output.*
- [ ] Retry policy for items that fail twice — drop, or queue for manual review?
  — owner: Viacheslav — *default: drop and record; the job still reaches the digest unenriched and
  simply ranks lower.*
- [ ] Should the Run cut-off be a fixed clock time or the previous Run's end?
  — owner: Viacheslav — *default: the previous Run's `cutoff_to`, so a skipped day is caught up rather
  than lost.*

## DoD self-check

- [x] Coverage types: happy (01, 10), error (07, 08, 09), authorization (12), domain invariant (02, 03, 04, 11), cross-context (05, 06)
- [x] No implementation tokens in §5 — no API names, no HTTP, no JSON, no SQL
- [x] Every US has ≥1 AC; NFRs measurable
- [x] Every open question has an owner and a default
