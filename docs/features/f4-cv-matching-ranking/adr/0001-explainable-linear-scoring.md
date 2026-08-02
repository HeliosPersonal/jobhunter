---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f4-cv-matching-ranking, jobhunter]
---

# F4-0001 — A transparent linear score, computed in code, not by the model

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The model produces a fit judgement per job. Something must turn a set of judgements into an order,
because the digest shows ten cards and the ordering *is* the product
([[../../../DECISION-LOG|D5]]). The question is where that ordering decision lives.

It is tempting to let the model do it — it has the most context, and one prompt is simpler than a
formula plus weights plus a preference model. But the ordering is the thing the Owner must trust at
07:00 while half awake, and trust requires being able to ask "why is this one first" and get an
answer.

## Decision drivers

- [[../../../CONTEXT]] invariant 4: no unexplained number reaches the Owner.
- The order must be **testable**. A golden ranking set is only possible if the ordering is a function
  of stored inputs.
- F7 will supply learned preference weights. They must influence the order without a prompt change,
  or every preference update becomes a model change with all the regression risk that implies.
- Tuning must not cost money. Re-ranking with different weights should be free and instant, not a
  re-run of a deep-tier batch.
- A model asked to rank 150 items in one call reasons worse than a model asked to judge one item
  150 times — and the batched per-item form is what the cost model already requires.

## Considered options

1. **The model returns the final rank** — one prompt, all jobs, "order these".
2. **The model returns a single score** and we sort by it directly.
3. **The model returns a per-job judgement; a linear formula in code combines it with preference
   weights and freshness.**
4. **A learned ranker** (gradient-boosted trees over engineered features), trained on Owner signals.

## Decision outcome

**Chosen: Option 3.**

```
final_score = 100 × (0.60·match + 0.25·preference + 0.15·freshness) × confidence
```

computed by `ScoreCalculator`, a static pure function whose every input is an explicit parameter — no
repository, no clock, no options object. Every component is persisted alongside the result, and a
test recomputes the total from the stored components and fails if it does not reconcile.

The weights are configuration with documented rationale: fit dominates at 0.60; preference gets 0.25,
enough to reorder within a band but never enough to bury a strong fit; freshness gets 0.15, because
being early is this product's structural advantage but a fortnight-old excellent role still belongs
in the digest.

Option 2 is close and tempting, but folding preferences into the prompt means every preference update
is a prompt change, and it makes the F7 feedback loop cost a deep-tier re-run. Option 1 additionally
degrades quality — ranking 150 items in one context is a harder task than judging one item well, done
150 times.

Option 4 is the right answer with 10 000 labelled examples. With a few hundred Signals it would
overfit to a month of accidents, and — decisively — it cannot answer "why is this first" in a sentence
the Owner can read. Revisit if the signal corpus ever reaches the thousands
([[../../../BACKLOG]] §4).

## Consequences

**Positive**
- Every ordering decision decomposes into named, stored components (QG-1).
- Deterministic and therefore property-testable and golden-set-testable (QG-3).
- Weights are tunable without a deploy, a prompt change or any spend.
- F7 plugs into one component; a preference update never risks a model regression.
- The digest can honestly say *why* a job is first, in one line.

**Negative**
- A linear combination cannot express interactions — "high AI usage matters only at senior level" is
  not representable. Accepted: the model's `match_score` already captures interactions within fit,
  which is where they mostly live.
- The weights are guesses until enough data exists to challenge them. Made visible by documenting the
  rationale and by tracking `precision@10` against them.

**Neutral**
- The formula is a natural place to add components later (application history, company research
  quality) without restructuring anything.

## Links

- [[../sad]] §10 QG-1, QG-3 · [[../contracts/match-schema]] §Ranking formula
- [[../../../DECISION-LOG|D5]] · [[../../../CONTEXT]] invariant 4
- [[../../f7-preference-learning/index|F7]] (supplies the preference component)
