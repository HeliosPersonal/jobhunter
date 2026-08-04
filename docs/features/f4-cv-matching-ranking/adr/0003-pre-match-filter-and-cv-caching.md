---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f4-cv-matching-ranking, jobhunter]
---

# F4-0003 — Pre-match filter and CV prompt caching

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Matching is the single most expensive thing the system does. Priced against Anthropic list rates on
2026-08-02 — `claude-sonnet-5` at $3.00/$15.00 per MTok, batch discount applied — sending all 150
newly discovered jobs through the deep tier costs **$1.58 of a $2.16 Run**: 73% of daily spend, and
roughly $47 of a $65 month.

That number was not visible when the cascade was designed. [[../../../DECISION-LOG|D4]] set a $2.00
per-Run ceiling on the assumption that a Run would cost a fraction of it; at $2.16 the ceiling is
not headroom, it is a binding constraint that would abort ordinary days.

Two facts make the cost avoidable rather than inherent:

1. **Enrichment already answers most of the disqualifying questions.** Salary band, remote policy,
   timezone band and contractor friendliness come out of the cheap tier at $0.0029 per job. A job
   that is onsite-only in the wrong hemisphere does not need a CV comparison to be ruled out — the
   deep tier would spend $0.0105 to reach a conclusion the cheap tier already reached.
2. **Every item in a matching batch shares a large identical prefix.** The system prompt (~400
   tokens) and the CV (~2 000 tokens) are byte-identical across all 150 items — about 47% of input.
   Prompt caching serves those at 0.1×.

## Decision drivers

- The ceiling must be headroom, not a constraint that clips normal operation ([[../../../CONTEXT]] invariant 6).
- Precision is the product ([[../../../DECISION-LOG|D5]]) — no saving is worth losing a job the
  Owner would have wanted.
- Whatever is skipped must be *visible*, not silently dropped ([[../../../CONTEXT]] invariant 11).
- The cheap tier's facts are already paid for. Not using them is waste, not caution.

## Considered options

1. **Match every enriched job at deep tier** (as originally designed).
2. **Move matching to the cheap tier.**
3. **Pre-match filter on enrichment facts + prompt-cache the CV prefix**, keeping deep tier for
   everything that passes.
4. **Cap matching at the top N by a heuristic pre-score.**

## Decision outcome

**Chosen: Option 3.**

**Pre-match filter.** After enrichment and before the matching batch is built, a job is excluded
from deep-tier matching when it fails a *hard, factual* disqualifier drawn from `enrichments` and
the active `Profile`:

| Rule | Excluded when |
|---|---|
| Timezone | Job's band is incompatible **and** the role is not remote |
| Employment type | Not in the Profile's `employment_types` |
| Seniority floor | Two or more levels below the Profile's seniority, **except** at an early company stage (`{Seed, SeriesA}` by default, config `PreMatch:SeniorityFloorExemptStages`) — T18 |
| Salary | Estimate below the floor **with `salary_confidence` ≥ 0.8** |
| Lifecycle | Job already closed, or already has a current match for this CV version |

Every rule is a *fact*, never a judgement — that is the line. "Probably not a good culture fit" is a
matching decision and stays in the deep tier. On the reference corpus these rules pass roughly 40%
of enriched jobs through.

**Precedence over post-ranking suppression.** Two of these disqualifiers —
timezone-incompatible-and-not-remote and employment-type-not-sought — previously also appeared as
post-ranking suppression rules in [[../contracts/match-schema|match-schema.md]] §Suppression. That is
a duplication with no precedence. This ADR is the **sole authoritative owner** of both: they are
decided **pre-match** (a job failing either never reaches the deep tier and never gets a match), and
the corresponding rows have been **removed from the post-ranking suppression table**. Post-ranking
suppression keeps only `final_score < 40`, the opt-in salary-floor down-weight, and F7's *learned*
preference rules. Factual exclusions run before matching; learned suppression runs after.

Three properties keep this from becoming a silent filter:

- **Excluded jobs still get a `scores` row**, with `suppressed = true` and a reason naming the rule —
  the same mechanism learned preferences use (invariant 11). They appear in the digest footer count
  and in `/hidden`.
- **The filter is bypassable.** `Run:MatchAllJobs = true` matches everything, for a weekly
  calibration run that measures what the filter would have excluded.
- **Regret is measured.** A weekly job samples 20 filtered-out jobs, matches them at cheap tier, and
  alerts if any would have scored above the presentation threshold. A non-zero rate means a rule is wrong.

**CV prompt caching.** The matching prompt is ordered so the stable prefix comes first — system
prompt, then CV, then the per-job role block — with a `cache_control` breakpoint at the end of the
CV. The prefix is ~2 400 tokens, comfortably above Sonnet 5's 1 024-token minimum. This constrains
the prompt builder: **nothing volatile may precede the breakpoint**, so no timestamps, no per-job
values, no run ids in the system prompt.

Combined effect at 150 jobs/day: matching falls from **$1.58 to $0.44**, and a Run from **$2.16 to
$1.03** — about **$31/month**. The $2.00 ceiling becomes genuine headroom.

Option 2 is rejected on quality — matching *is* the product ([[0001-explainable-linear-scoring|ADR-F4-0001]]).
Option 4 is rejected because a heuristic pre-score is an unexplainable judgement, while these rules
are checkable facts the Owner can read.

## Consequences

**Positive**
- Run cost drops ~52%; monthly spend ~$31 instead of ~$65.
- The ceiling stops being a constraint on normal operation, which is what makes invariant 6 a
  safety net rather than a daily nuisance.
- Every exclusion is factual, attributable to a named rule, counted in the digest, and retrievable.
- Spend scales with *jobs worth judging* rather than jobs discovered, so widening the company
  registry no longer scales cost linearly.

**Negative**
- A wrong rule silently removes a job from consideration. This is the real risk, and it is why the
  regret sampler and the bypass flag are part of the decision rather than follow-up work.
- The filter's rules duplicate logic that also exists in suppression ([[../../f7-preference-learning/index|F7]]).
  Bounded by keeping the pre-match rules strictly factual and preference rules strictly learned.
- Prompt-cache correctness is now load-bearing for the cost model. A silent invalidator would
  restore the old bill without failing anything — hence the assertion below.

**Neutral**
- Cache-hit rate is asserted in CI: an integration test over a 20-item batch requires
  `cache_read_input_tokens > 0` on every item after the first. Cache economics also mean the batch
  must not be split arbitrarily — items sharing a CV belong in one submission.

## Links

- [[../PRD]] §6 NFRs, AC-12, AC-13 · [[../../../DECISION-LOG|D4]] · [[../../../CONTEXT]] invariants 6, 11
- [[../../../operations/infrastructure]] §8 · [[../../f3-claude-batch-enrichment/contracts/enrichment-schema|F3 cost model]]
- [[0001-explainable-linear-scoring|ADR-F4-0001]]
