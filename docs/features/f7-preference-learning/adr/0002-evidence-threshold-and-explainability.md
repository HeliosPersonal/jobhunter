---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f7-preference-learning, jobhunter]
---

# F7-0002 — No weight without cited evidence; 200 signals before activation

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Two failure modes threaten a preference learner in a single-user system, and they are opposite.

**Learning too early.** With twelve signals, every dimension has a rate. Fitting them produces
confident weights encoding the first week's accidents — the Owner happened to ignore three Berlin
roles on a busy Tuesday, and Berlin is now suppressed for months.

**Learning invisibly.** A weight that suppresses jobs without the Owner being able to see it, question
it or switch it off is indistinguishable from a bug. The first time they suspect the system is hiding
something good, they stop trusting the digest — and a digest that is not trusted is not read.

[[../../../DECISION-LOG|D6]] already decided *when* to activate. This ADR records the thresholds and
the explainability contract that make the decision operational.

## Decision drivers

- [[../../../CONTEXT]] invariant 11 requires a recorded reason for every suppression.
- The retention argument from [[../../../DECISION-LOG|D7]] runs the other way too: a visible filter is
  the strongest reason to keep triaging, so visibility is a feature, not a compliance cost.
- The Owner must be able to disagree with a specific preference (AC-06), which requires it to be
  addressable and its basis inspectable.
- A wrong weight must be diagnosable without re-running the fit.

## Considered options

1. **Activate immediately**, refine as evidence accumulates.
2. **Activate at a signal threshold**, with weights citing their evidence.
3. **Activate at a statistical-confidence threshold** per dimension.
4. **Never activate automatically** — propose preferences and require the Owner to accept each.

## Decision outcome

**Chosen: Option 2**, with three concrete thresholds:

| Threshold | Value | Why |
|---|---|---|
| Model activation | **200 signals** | Roughly two weeks of normal use. Enough that a single bad day cannot dominate |
| Weight evidence floor | **≥ 3 supporting signals** per value | Below three, a rate is a coincidence, not a preference |
| Dimension bound | **≤ 0.40** of the preference component | No single dimension can become the ordering |

And an explainability contract every weight must satisfy:

- `supporting_signal_ids` — the actual evidence, by id.
- `supporting_signal_count` and `positive_rate` — stored, not recomputed, so the explanation stays
  stable after the evidence window moves on.
- A one-sentence rendering: *"34 of your last 38 ignores were below 170k EUR."*
- One-tap disable, honoured immediately, and not relearned until the supporting evidence doubles.

Below 200 signals no model is activated and the reason is **recorded** — `insufficient evidence: 143
signals` — so the absence of learning is visible rather than mysterious.

Option 3 is statistically more principled but produces a threshold nobody can explain, and the
explanation is the point. Option 4 is safest and would not be used: a weekly approval queue is exactly
the kind of admin this product exists to remove.

## Consequences

**Positive**
- No preference exists without evidence that can be inspected in one query.
- The Owner can always answer "why was this hidden" and act on the answer.
- The visible-suppression sentence in the digest turns ignoring into engagement
  ([[../../../DECISION-LOG|D7]]).
- The absence of learning is explained, not silent.

**Negative**
- The first two weeks show no learning at all. Acceptable — signal capture began at the very first
  digest ([[../../../DECISION-LOG|D6]]), so nothing is lost, only deferred.
- Rare dimension values never earn a weight. Correct: three observations is not a preference.
- Storing signal ids per weight adds rows. Trivial at this scale, and it is the entire mechanism.

**Neutral**
- The thresholds are configuration, so they can be tuned once real behaviour is observed — but the
  *requirement* for evidence is not configurable.

## Links

- [[../../../CONTEXT]] invariant 11 · [[../../../DECISION-LOG|D6, D7]]
- [[../PRD]] AC-02, AC-03, AC-09 · [[../sad]] §10 QG-1, QG-3
- [[../data-model]] §preference_weights
