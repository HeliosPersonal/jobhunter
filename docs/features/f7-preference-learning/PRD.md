---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f7-preference-learning, mvp, jobhunter]
---

# PRD — f7-preference-learning

> **Inputs:** [[../../CONTEXT]] §1 (Signal, PreferenceModel), invariant 11 · [[../../00-overview/idea-brief|idea-brief]] §8
> **External context:** [[../../DECISION-LOG|D6, D7]], [[../../ARCHITECTURE-OPEN-DECISIONS|O5]]

## 1. Context

The Owner knows within two seconds that a role is wrong. That judgement is made 150 times a day and,
without F7, thrown away 150 times a day. Meanwhile the same forty job archetypes keep reappearing week
after week, and the Owner keeps making the same forty judgements.

The premise is simple: the taps already contain the preferences. Salary floors, country tolerances,
company-size aversions, technology enthusiasms — none of these need to be stated if they can be
inferred from a few hundred decisions.

Two things make this harder than it sounds, and both shape the design.

**First, silent filtering destroys trust.** A learned filter the Owner cannot see is
indistinguishable from a bug, and the first time they suspect the system is hiding something good,
they stop believing the digest. Hence [[../../CONTEXT]] invariant 11: suppression always records a
reason, and the digest always reports the count. Turned around, this becomes the product's strongest
retention mechanism — "I stopped showing you 34 jobs below your salary floor" makes ignoring feel
productive rather than futile ([[../../DECISION-LOG|D7]]).

**Second, learning from too little evidence produces confident nonsense.** Twelve signals fitted into
weights would encode the first week's accidents as permanent preferences. Hence the split decided in
[[../../DECISION-LOG|D6]]: capture from the very first digest (F5 already does), activate only at 200
signals.

## 2. Goals

- Infer the Owner's preferences from what they do, across salary, geography, company size, technology
  and timezone.
- Improve tomorrow's ordering measurably over yesterday's.
- Explain every learned weight by the evidence that produced it.
- Report every suppression, always, in the digest.
- Never let a learned rule silently override something the Owner stated explicitly.

## 3. Non-goals

- Capturing the signals — F5 and F6 already do; F7 consumes them.
- Changing the ranking formula — F4 owns it; F7 supplies one component
  ([[../f4-cv-matching-ranking/adr/0001-explainable-linear-scoring|ADR-F4-0001]]).
- A learned ranker or any opaque model. Explainability is a hard requirement, not a preference.
- Learning across users. One Owner ([[../../CONTEXT]] invariant 9).
- Real-time adaptation. Weekly refit is fast enough for a daily product and far more stable.

## 4. User stories

### US-01: Stop showing me things I always reject
**As the** Owner **I want** the system to notice what I consistently ignore **so that** I stop making
the same judgement every week.

### US-02: Know what was hidden and why
**As the** Owner **I want** every hidden opportunity counted and explained **so that** I can tell a
working filter from a broken one.

### US-03: Understand a learned preference
**As the** Owner **I want** to see which of my actions produced a preference **so that** I can judge
whether the system understood me correctly.

### US-04: Correct a wrong preference
**As the** Owner **I want** to disable or reverse a learned preference **so that** one bad week does
not permanently narrow what I see.

### US-05: Not be narrowed prematurely
**As the** Owner **I want** the system to wait until it has enough evidence **so that** it does not
learn from a handful of accidents.

### US-06: Have my explicit choices respected
**As the** Owner **I want** what I stated outright to outrank what was inferred **so that** the system
never argues with me.

### US-07: See that it is working
**As the** Owner **I want** to see whether the ordering is improving **so that** I know the learning
is worth having.

## 5. Acceptance criteria

### AC-01 (US-01) — happy path
**Given** enough recorded evidence
**When** preferences are recomputed
**Then** a new set of weights is produced across the supported dimensions and becomes the one in use.

### AC-02 (US-05) — domain invariant
**Given** less evidence than the required minimum
**When** recomputation runs
**Then** no new weights are produced, the previous behaviour continues unchanged, and the reason is
recorded.

### AC-03 (US-03) — domain invariant
**Given** any learned weight
**When** it is inspected
**Then** the specific recorded actions that produced it are identifiable.

### AC-04 (US-02) — domain invariant
**Given** an opportunity hidden because of a learned preference
**When** the daily summary is produced
**Then** the count and the reason are stated, and the opportunity remains retrievable.

### AC-05 (US-06) — domain invariant
**Given** a preference the Owner stated explicitly and an inferred preference that contradicts it
**When** ordering is computed
**Then** the explicit preference wins, and the conflict is recorded.

### AC-06 (US-04) — happy path
**Given** a learned preference the Owner disagrees with
**When** they disable it
**Then** it stops affecting ordering immediately and is not relearned until substantially new evidence
appears.

### AC-07 (US-04) — cross-context
**Given** learning has been disabled entirely
**When** ordering is computed
**Then** only explicit preferences apply, and the daily summary states that learning is off.

### AC-08 (US-07) — happy path
**Given** several weeks of operation
**When** performance is reviewed
**Then** the ordering quality before and after learning was activated is comparable from recorded data.

### AC-09 (US-01) — error path
**Given** evidence that is overwhelmingly one-sided in a dimension
**When** weights are computed
**Then** the resulting effect is bounded, so no single dimension can dominate the ordering.

### AC-10 (US-03) — authorization
**Given** a request to inspect, disable or reset learned preferences
**When** it arrives from anyone other than the Owner
**Then** it is refused and nothing changes.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Evidence threshold | 200 signals before first activation | Configuration, asserted |
| Refit duration | < 30 s for 5 000 signals | Benchmark |
| Refit cadence | weekly, Monday 03:00 Europe/Kyiv | Schedule test |
| Evidence window | 180 days, recency-weighted | Configuration, asserted |
| Weight bound | no dimension exceeds 40% of the preference component | Property test (AC-09) |
| Explainability | 100% of weights cite ≥ 3 signals | Assertion on every weight |
| Suppression reporting | 100% of suppressions counted in the digest | Integration test |
| `precision@10` improvement | measurably above the M4 baseline within 8 weeks | Weekly metric |

## 6.1 Security / privacy

- **Data classification:** confidential — the signals reveal what the Owner wants and rejects.
- **Personal data touched:** the Owner's behaviour. No CV content ever enters a signal.
- **AuthZ/AuthN impact:** inspecting, disabling and resetting preferences are owner-scoped (AC-10).
- **Abuse cases:**
  - Preferences narrowing the digest to nothing → weights are bounded (AC-09) and a floor on delivered
    cards means the digest is never empty because of learning alone.
  - A learned rule contradicting an explicit one → explicit always wins, and the conflict is recorded (AC-05).
  - Signal facts drifting as jobs are edited → facts are snapshotted at the moment of the action, so
    history cannot be rewritten.
- **Security review:** N/A — no new external surface, no new data category.

## 7. Metrics / KPIs

- **`precision@10` before and after activation** — the headline. Target: measurably better within
  eight weeks (AC-08).
- **Suppression rate** — reported, not targeted. A sharp rise means over-fitting.
- **Suppression regret** — how often the Owner retrieves and acts on a suppressed opportunity.
  Target near zero; any sustained non-zero means a weight is wrong.
- **Weights disabled by the Owner** — a direct measure of whether the inference is any good.

## 8. Open questions

- [ ] Should the salary floor become a hard filter once learned confidently? — owner: Viacheslav —
  *default: no; down-weight only unless explicitly opted in.* ([[../../ARCHITECTURE-OPEN-DECISIONS|O5]])
- [ ] Recency weighting half-life within the 180-day window — owner: Viacheslav — *default: 60 days.*
- [ ] Should a disabled weight ever be relearned? — owner: Viacheslav — *default: only if the
  supporting evidence doubles after the disable.*

## DoD self-check

- [x] Coverage types: happy (01, 06, 08), error (09), authorization (10), domain invariant (02, 03, 04, 05), cross-context (07)
- [x] No implementation tokens in §5
- [x] Every US has ≥1 AC; NFRs measurable
