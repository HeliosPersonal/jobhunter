---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "13"
ticket: ""
tags: [sdlc/stage-13, feature/f7-preference-learning, mvp, jobhunter]
---

# Epic — F7 Preference Learning

Turn several hundred recorded actions into bounded, explainable weights that reorder tomorrow's
digest — and into the sentence that makes ignoring feel productive: *"I stopped showing you 34 jobs
below your salary floor."*

Two constraints shape everything here:

1. **Explainability is a hard requirement.** A learned filter the Owner cannot see, question or switch
   off is indistinguishable from a bug ([[../../../CONTEXT]] invariant 11). Every weight cites the
   actions that produced it.
2. **Evidence before inference.** Twelve signals fitted into weights encode a week's accidents as
   permanent preferences. Capture began with the first digest; activation waits for 200 signals
   ([[../../../DECISION-LOG|D6]]).

## Upstream (link, don't duplicate)

- PRD: [[../PRD|PRD]] — US-01…US-07, AC-01…AC-10
- SAD: [[../sad|sad]] — fitting method, evidence window, activation, precedence
- Data model: [[../data-model|data-model]] — `signals`, `preference_models`, `preference_weights`, `suppression_overrides`
- Test plan: [[../test-plan|test-plan]] — the nine-profile synthetic corpus
- ADRs: [[../adr/0001-transparent-frequency-weighting|F7-0001]], [[../adr/0002-evidence-threshold-and-explainability|F7-0002]]
- Consumers: [[../../f4-cv-matching-ranking/index|F4]] (the preference component), [[../../f5-daily-digest-telegram/index|F5]] (the footer)

## Scope

**In:** signal storage and weighting, the fitting method, model versioning and atomic activation,
suppression rules and the card floor, the explainability view, Owner overrides, precision tracking.
**Out:** signal capture (F5, F6), the ranking formula (F4 — F7 supplies one component), the digest
text (F5), any learned ranker or opaque model (explicitly rejected).

## Module scope

`Domain/Preferences`, `Application/Preferences`, `Infrastructure/Persistence` (four tables),
four endpoints on `JobHunter.Api`, two commands in `JobHunter.Telegram`.

## Handoff interfaces

| Produces | Consumer |
|---|---|
| `PreferenceModelUpdated` | F4 ranking |
| Preference component + per-dimension contributions | F4 `ScoreCalculator` |
| Suppression reasons and counts | F5 digest footer |
| `/hidden` view | the Owner, and the suppression-regret metric |

## Tasks

See [[tracker|tracker]]. 9 tasks, ≈ 5 person-days.

## Definition of Done (epic)

- AC-01…AC-10 covered by passing tests.
- **All nine synthetic profiles pass**, including the indifferent one that must produce no weights —
  the test that separates learning from superstition.
- Every persisted weight cites at least three signals and renders as one readable sentence.
- No dimension exceeds 0.40 of the preference component under any adversarial distribution.
- Suppression never empties the digest: the card floor holds.
- Every suppression is counted and explained in the digest footer.
- A `precision@10` series before and after activation is queryable, so the value of this feature is
  measurable rather than assumed.
- Contributes to milestone M5 in [[../../../BACKLOG|BACKLOG]] §1.
