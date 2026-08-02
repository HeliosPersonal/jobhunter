---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f7-preference-learning, jobhunter]
---

# F7-0001 — Transparent frequency weighting, not a learned ranker

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Several hundred recorded actions must become weights that reorder the digest. The obvious modern
answer is a small learned ranker over engineered features, trained on the signals.

Two things argue against it, and one of them is decisive.

The first is data volume. After six months of daily use the corpus is perhaps 3 000 signals across
seven dimensions, heavily imbalanced (most actions are ignores) and non-stationary (the Owner's
target shifts as the search progresses). That is not enough to train anything that generalises; it is
enough to memorise a month of accidents.

The second, and decisive, is that [[../../../CONTEXT]] invariant 11 requires every suppression to
carry a reason the Owner can read. Not a feature importance — a sentence. A gradient-boosted model
can produce a SHAP value; it cannot produce *"34 of your last 38 ignores were below 170k"*.

## Decision drivers

- Explainability is a hard requirement, not a nice-to-have ([[../../../DECISION-LOG|D7]]).
- The evidence volume will not support a learned model for years, if ever.
- The Owner must be able to disagree with a specific preference and switch it off (AC-06), which
  requires preferences to be discrete and addressable.
- Whatever is chosen must be testable without real data, or it cannot be developed at all before it
  has been running for six months.

## Considered options

1. **Frequency weighting per dimension value**, recency-weighted, bounded, with cited evidence.
2. **Logistic regression** over one-hot dimension features.
3. **Gradient-boosted trees** over engineered features.
4. **A model-based approach** — ask Claude to infer preferences from the signal history.

## Decision outcome

**Chosen: Option 1.**

For each dimension value, compute the recency-weighted positive rate across signals mentioning it —
where *positive* means saved, applied, interviewed or offered, and *negative* means ignored or
rejected, each carrying its own signal weight. Map that rate to a weight in `[−1, +1]`, bound each
dimension's total contribution at 0.40 of the preference component, and normalise across dimensions.

Three rules keep it honest:

1. **A value needs ≥ 3 supporting signals** to earn a weight at all. Below that there is a rate but no
   evidence, and a rate without evidence is a coincidence.
2. **Every weight stores the ids of the signals that produced it**, which is what makes the one-sentence
   explanation possible ([[0002-evidence-threshold-and-explainability|ADR-F7-0002]]).
3. **`WeightFitter` is a pure function**, so a fictional Owner with known preferences can be simulated
   and the fitter asserted to recover them — including the case where it must recover *nothing* from
   noise.

Options 2 and 3 both fail the explanation requirement and the data-volume reality. Option 4 is
interesting but adds cost and non-determinism to a weekly job whose output must be stable and
auditable — and it would produce prose, not addressable weights the Owner can switch off individually.

## Consequences

**Positive**
- Every preference is a row with a dimension, a value, a rate and its evidence. The explanation writes
  itself.
- The Owner can disable one specific preference without affecting any other (AC-06).
- Fully deterministic, so the synthetic-behaviour corpus is a real regression suite.
- Cheap: a weekly aggregation over a few thousand rows, no training, no inference cost.
- Fails gracefully. With no data, the weights are absent and F4 renormalises.

**Negative**
- Cannot express interactions — "high AI usage matters only for senior roles" is not representable.
  Accepted, and stated: F4's `match_score` already captures interaction within fit, which is where
  most of it lives.
- Correlated dimensions can double-count. Mitigated by normalisation across dimensions and asserted by
  the correlated profile in the corpus.
- A genuinely non-linear preference is invisible. Revisit only if the signal corpus reaches the
  thousands ([[../../../BACKLOG]] §4).

**Neutral**
- The method is simple enough to explain to a reviewer in two sentences, which for a portfolio artifact
  is a feature rather than a limitation.

## Links

- [[../PRD]] §3, AC-03 · [[../sad]] §4 S1, §10 QG-1
- [[../test-plan]] §The synthetic-behaviour corpus
- [[../../f4-cv-matching-ranking/adr/0001-explainable-linear-scoring|ADR-F4-0001]] (the same reasoning, one layer up)
