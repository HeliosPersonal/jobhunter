---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f5-daily-digest-telegram, mvp, jobhunter]
---

# Epic — F5 Daily Digest & Telegram Delivery

One message sequence at 07:00 Europe/Kyiv that answers "is today worth my attention" in three seconds,
followed by scannable cards with four one-tap actions — and the machinery that makes it arrive
every single morning regardless of what failed upstream.

**This is milestone M4, the first shippable release.** Everything before it is machinery; this is the
product.

Three properties define it:

1. **07:00, always.** A partial digest on time beats a complete one late. Silence is never an outcome.
2. **Exactly once.** A card delivered twice in a morning digest looks like a broken system in the one
   artifact the Owner judges everything by.
3. **Ignoring feels productive.** The digest reports what it hid and why, so triage becomes engagement
   rather than churn.

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-08, AC-01…AC-12
- SAD: [[../sad|sad]] — assembly, delivery idempotence, callback handling
- Data model: [[../data-model|data-model]] — `digests`, `digest_cards`, `delivery_log`
- Contract: [[../contracts/telegram-messages|Telegram messages]] — layout, payloads, escaping, commands
- Test plan: [[../test-plan|test-plan]] — the rendering corpus and the duplicate-delivery suite
- ADRs: [[../adr/0001-never-delay-the-digest|F5-0001]], [[../adr/0002-delivery-idempotence|F5-0002]]
- Reused: [[../../f3-claude-batch-enrichment/sad|F3]] batch machinery for the narrative call

## Scope

**In:** digest assembly, apply-link verification, narrative synthesis with template fallback, message
rendering and escaping, delivery with per-card idempotence, the 06:45/07:00 schedule and every
degraded variant, callback handling, Signal capture, the command set.
**Out:** ranking (F4), application status rules beyond the initial action (F6), preference fitting
(F7 — F5 captures the evidence), search itself (F9 — F5 renders its results).

## Module scope

`Domain/Reporting`, `Application/Reporting`, `Application/Delivery`, the whole `JobHunter.Telegram`
host, `JobHunter.Claude/Prompts/DigestNarrativePrompt.cs`, `Infrastructure/Persistence` (three tables).

## Handoff interfaces

| Produces | Consumer |
|---|---|
| `DigestReady` | the Telegram host |
| `DigestDelivered` | metrics |
| `OwnerActionRecorded` | F6 application tracking, F7 signal capture |
| `signals` rows | F7 (which owns the schema) |
| Card rendering | F9 search results, F6 pipeline view |

## Tasks

See [[tracker|tracker]]. 13 tasks, ≈ 6.5 person-days (base 6.25 + the `NearDuplicateGrouper` task
relocated from F2 per [[../../f2-normalization-dedup/adr/0001-conservative-fingerprint|ADR-F2-0001]]).

## Definition of Done (epic)

- AC-01…AC-12 covered by passing tests.
- **Every degraded path delivers a digest** — no jobs, incomplete analysis, cost abort, no Run at all.
- **Zero duplicate deliveries** across every case in the duplicate-delivery suite, including
  mid-delivery kills.
- The rendering corpus is green, including every hostile-input case, with committed snapshots.
- Delivery lands at 07:00 ±3 min across both DST transitions.
- Every action is acknowledged in under a second, including on closed and missing jobs.
- Signals are captured from the very first digest, so F7 has evidence to learn from.
- **The live-smoke checklist has been executed against a real Telegram client** — buttons verified
  working in the real app.
- **Milestone M4 complete** ([[../../../BACKLOG|BACKLOG]] §1): a real digest lands at 07:00 with
  working Open/Ignore/Save/Applied buttons.
