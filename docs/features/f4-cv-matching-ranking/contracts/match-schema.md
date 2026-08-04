---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, feature/f4-cv-matching-ranking, mvp, jobhunter]
---

# Match output contract

> Schema, prompt, CV handling rules and the ranking formula. Changing anything here bumps
> `PromptVersion` and requires an updated golden ranking set (gate G10).

## Output record

```csharp
public sealed record MatchOutput(
    int MatchScore,                          // 0-100, the model's judgement
    InterviewProbability InterviewProbability, // Low | Moderate | Good | Strong
    IReadOnlyList<string> MissingSkills,      // may be empty; empty is meaningful
    SalaryExpectationDto? SalaryExpectation,  // what the Owner could plausibly ask for THIS role
    IReadOnlyList<string> Reasons);           // >= 1, else rejected
```

```json
{
  "type": "object",
  "required": ["matchScore", "interviewProbability", "missingSkills", "reasons"],
  "properties": {
    "matchScore":           { "type": "integer", "minimum": 0, "maximum": 100 },
    "interviewProbability": { "enum": ["Low", "Moderate", "Good", "Strong"] },
    "missingSkills":        { "type": "array", "items": { "type": "string" }, "maxItems": 10 },
    "salaryExpectation": {
      "type": ["object", "null"],
      "required": ["min", "max", "currency", "period"],
      "properties": {
        "min":      { "type": "number", "minimum": 0 },
        "max":      { "type": "number", "minimum": 0 },
        "currency": { "type": "string", "pattern": "^[A-Z]{3}$" },
        "period":   { "enum": ["Year", "Month", "Day", "Hour"] }
      }
    },
    "reasons": { "type": "array", "items": { "type": "string" }, "minItems": 1, "maxItems": 5 }
  }
}
```

**Interview probability is a band, not a number.** A model asked for "62%" will produce one, and it
will be false precision the Owner may act on. Four bands are honest about the resolution actually
available, and can be calibrated against outcomes later (SAD §11 D4).

## Prompt

`JobHunter.Claude/Prompts/MatchPrompt.cs`, `PromptVersion = "match-v1"`.
**This file is the only place in the codebase where CV text is rendered into a string.**

**System**

```text
You assess how well a specific candidate fits a specific engineering role. You are blunt and
calibrated. Your value is in saying no clearly.

Rules:
- Compare the candidate's demonstrated experience against what the role requires. Weight what they
  have actually done far above what they list as a skill.
- matchScore is fit, not desirability. A perfect fit for a mediocre role scores high.
- Missing skills means genuinely required and genuinely absent. Do not list nice-to-haves. An empty
  list is a valid and useful answer.
- interviewProbability accounts for seniority gap, domain gap, location and visa constraints, and
  how competitive the role is. Be pessimistic: the candidate would rather be surprised upward.
- salaryExpectation is what THIS candidate could plausibly ask for THIS role given their level and
  the market implied by the posting. Null if the posting gives you nothing to anchor on.
- Every reason must be specific and reference something concrete from either the CV or the posting.
  "Good fit" is not a reason. "Seven years of Kafka against a role that names Kafka as core" is.
- If the role is a poor fit, say so plainly and score it low. A generous score is a disservice.
```

**User** (per item)

```text
--- CANDIDATE ---
{cvText, truncated to 8000 characters at a section boundary}
Salary floor: {salaryFloor} {currency}
Timezone: {ownerTimezoneBand}
Open to: {employmentTypes}
--- END CANDIDATE ---

--- ROLE ---
Company: {companyName} · Stage: {companyStage}
Title: {title} · Seniority: {seniority}
Location: {locationSummary} · Remote: {remotePolicy} · Timezone: {timezoneBand}
Employment: {employmentType} · Contractor friendly: {isContractorFriendly}
Published salary: {publishedSalary}
Estimated salary: {enrichmentSalaryEstimate} (confidence {salaryConfidence})
Technologies: {technologies}
AI usage: {aiUsage}

{description, truncated to 10000 characters at a paragraph boundary}
--- END ROLE ---
```

When the enrichment is missing (AC-09) the enrichment-derived lines are omitted entirely rather than
filled with `Unknown`, and the resulting score is multiplied by a 0.85 confidence factor. A prompt
padded with "Unknown" invites the model to reason about the unknowns; omitting the lines does not.

## Prompt caching (contract constraint)

The matching prompt is ordered so the stable prefix comes first — **system prompt, then the CV, then
the per-item role block** — with a **`cache_control` breakpoint at the end of the CV**. The prefix
(~2 400 tokens) is byte-identical across every item in a matching batch and is served at 0.1× on
cache hit ([[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]]).

This is **load-bearing for the cost model** (R-05): a silently invalidated cache restores the old
bill without failing anything. Two constraints on `MatchPrompt` follow and are part of this contract,
not implementation detail:

1. **Nothing volatile may precede the breakpoint.** No timestamps, no run ids, no per-job values may
   appear in the system prompt or the CV block. Anything that changes per item or per Run goes in the
   role block, **after** the breakpoint. A volatile value before the breakpoint is a contract
   violation, caught by the snapshot assertion in T13.
2. **Items sharing a CV version are submitted as one batch.** Splitting them defeats the shared
   prefix, so the submitter offers no public way to split a CV's items across submissions.

CI asserts `cache_read_input_tokens > 0` on every item after the first over a 20-item batch (T13); a
deliberately introduced volatile value before the breakpoint must make that assertion fail.

## CV handling rules

Non-negotiable, and the subject of the QG-2 leakage suite:

1. CV text is loaded, passed **by value** into `MatchPrompt.Build(...)`, and released. It is never
   placed on a context object, an options object, a log scope or an `Activity` tag.
2. `MatchPrompt` has no logger and no telemetry dependency — it cannot emit even by accident.
3. The rendered prompt is **not** stored. `batch_items.raw_result` holds only the *response*, and
   only for failed items.
4. Exception messages from the matching path never include prompt content. A dedicated test asserts
   this by forcing failures with a sentinel-laden CV.
5. The leakage suite seeds the CV with unique sentinel tokens, runs a full pipeline, and greps every
   log line, span attribute, Typesense document, Telegram message and API response for them. Any hit
   fails the build.

## Ranking formula

Computed by `ScoreCalculator`, never by the model ([[../adr/0001-explainable-linear-scoring|ADR-F4-0001]]).

> **Done (TUNE-01, F4 T14):** the explainable `alignment` component below realised the planned re-weight
> from `0.60·match + 0.25·preference + 0.15·freshness`.
> **Done (TUNE-02, F4 T15):** the `antiGoal` multiplier below down-weights (default) or, opt-in,
> suppresses roles the Owner is deliberately leaving. The general non-target-family filter (TUNE-06, T17)
> remains planned — see the [[../../../reviews/career-alignment-tuning-backlog|tuning backlog]].

```
match_component      = matchScore / 100
alignment_component  = 0.5·aiUsageScore + 0.5·roleFamilyTierScore, in [0, 1]
preference_component = Σ over dimensions: weight(dimension, jobValue), clamped to [0, 1]
freshness_component  = exp(-ageDays / 7)
confidence           = enrichment present ? 1.00 : 0.85
antiGoal             = anti-goal role ? penalty factor (default 0.50) : 1.00

final_score = 100 × (0.45·match + 0.20·alignment + 0.20·preference + 0.15·freshness) × confidence × antiGoal
```

`alignment` rewards the Owner's AI-platform / platform trajectory, computed by `AlignmentCalculator` from
the F3 enrichment signals (TUNE-03): `aiUsageScore` is a monotone map of `AiUsage`
(None = 0, Low = 0.25, Medium = 0.60, High = 1.00) and `roleFamilyTierScore` is the `RoleFamily` tier
(Tier-1 `{AiPlatform, Platform, AiApplications, ForwardDeployed, FoundingEng}` = 1.00; Tier-2
`{BackendGeneric, Fullstack, DevOpsSRE}` = 0.70; Tier-3 `{Frontend, MlResearch, DataScience, PromptEng,
Other}` = 0.40; anti-goal `EnterpriseCrud` = 0.00). The two axes are blended equally, so a Tier-1
high-AI-usage role scores 1.00 and a no-AI anti-goal role scores 0.00 exactly. A job with no backing
enrichment degrades to the lowest-alignment inputs (None / Other). The component carries a reason
(invariant 4). When no preference model is active its 0.20 weight renormalises across the other three, so
the effective weights become `0.5625·match + 0.25·alignment + 0.1875·freshness`.

`antiGoal` (TUNE-02, T15) is a second, parallel multiplier — like `confidence`, it scales the whole
weighted total rather than being one weighted term — that down-weights a role the Owner is deliberately
leaving: low AI usage (`AiUsage ∈ {None, Low, Unknown}`) on the `EnterpriseCrud` family, classified by
`AntiGoalClassifier`. It is 1.00 for every other role and a configured penalty factor (`Ranking:AntiGoalPenaltyFactor`,
default 0.50) for an anti-goal one. The multiplier is **stored**, so the total still reconciles from its
components (QG-1) — the down-weight is never a silent adjustment. The guard is deliberately narrow: an
enterprise-CRUD posting that genuinely involves AI, or a low-AI role in another family, is not caught here
(the general non-target-family filter is TUNE-06/T17). Because alignment already pins `EnterpriseCrud` at
0, even a perfect-fit anti-goal role tops out at `100 × 0.75 × 0.50 = 37.5` under the default penalty,
below the presentation threshold — so the down-weight alone reasons it out of the digest, and the Owner
can opt into an explicit `Anti-goal role family: {family}` suppression instead.

| Component | Weight | Rationale |
|---|---|---|
| Match | 0.45 | Fit still leads, but no longer buries the Owner's trajectory on its own |
| Alignment | 0.20 | Rewards the AI-platform / platform career goal, explainably, from structured signals |
| Preference | 0.20 | Enough to reorder within a band, never enough to bury a strong fit |
| Freshness | 0.15 | Recency matters — an ATS-first product's whole advantage is being early — but it must not outrank fit |

Freshness decay: a job seen today scores 1.00, three days old 0.65, a week old 0.37, two weeks 0.14.
A fortnight-old posting is still visible if the fit is excellent, which is the intended behaviour.

**Suppression** (invariant 11 — always with a reason, never a silent filter). These are the
**post-ranking** suppression rules, applied to jobs that *were* matched:

| Rule | Reason recorded |
|---|---|
| Anti-goal role, opt-in enabled (`Ranking:AntiGoalSuppression`) | `Anti-goal role family: {family}` |
| `final_score < 40` | `Below presentation threshold` |
| Salary estimate below floor, high confidence, opt-in enabled | `Below salary floor ({amount})` |
| Learned preference hard rule (F7) | `Learned preference: {dimension} = {value}` |

The anti-goal rule is reported first because it names the deliberate policy the Owner set up, which is more
informative than the generic threshold; it is off by default (the anti-goal signal is a down-weight, not a
filter, until opted in) and, like every suppression, leaves the job retrievable via `/hidden` and counted
in the footer (invariant 11).

> **Planned change (TUNE-06, F4 T17):** add reason-logged down-weight (default) or opt-in suppression rows
> for the general non-target families (`Not a target role family: {family}`, for
> `roleFamily ∈ {MlResearch, DataScience, PromptEng}`), retrievable via `/hidden` and counted in the
> footer (invariant 11). See the [[../../../reviews/career-alignment-tuning-backlog|tuning backlog]].

Suppressed jobs are **counted and reported** in the digest footer, never silently dropped.

### Precedence: pre-match filter vs post-ranking suppression

Two disqualifiers — **timezone-incompatible-and-not-remote** and **employment-type-not-sought** — are
purely factual and are decided *before* matching, by the **pre-match filter**
([[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]]), not here. A job failing either is
excluded from the deep tier and gets a `scores` row with `suppressed = true` and the rule reason
(`Timezone incompatible` / `Employment type not sought`) **without ever being matched** (AC-12).

They are therefore **not** post-ranking suppression rules and have been removed from the table above
to keep one authoritative location: **the pre-match filter (ADR-F4-0003) is the sole owner of these
two factual exclusions.** The rules in the table above apply only to jobs that passed the pre-match
filter and were matched. The only overlap that remains is deliberate: F7's *learned* preference rules
(post-ranking) are distinct from the pre-match *factual* rules — factual before, learned after.

## Related

[[../sad]] §8 · [[../test-plan]] · [[../adr/0001-explainable-linear-scoring|ADR-F4-0001]] ·
[[../../../engineering/security]] §1
