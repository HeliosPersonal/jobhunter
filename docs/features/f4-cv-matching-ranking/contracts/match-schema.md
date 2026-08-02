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

```
match_component      = matchScore / 100
preference_component = Σ over dimensions: weight(dimension, jobValue), clamped to [0, 1]
freshness_component  = exp(-ageDays / 7)
confidence           = enrichment present ? 1.00 : 0.85

final_score = 100 × (0.60·match + 0.25·preference + 0.15·freshness) × confidence
```

| Component | Weight | Rationale |
|---|---|---|
| Match | 0.60 | Fit dominates. Everything else is a modifier |
| Preference | 0.25 | Enough to reorder within a band, never enough to bury a strong fit |
| Freshness | 0.15 | Recency matters — an ATS-first product's whole advantage is being early — but it must not outrank fit |

Freshness decay: a job seen today scores 1.00, three days old 0.65, a week old 0.37, two weeks 0.14.
A fortnight-old posting is still visible if the fit is excellent, which is the intended behaviour.

**Suppression** (invariant 11 — always with a reason, never a silent filter):

| Rule | Reason recorded |
|---|---|
| `final_score < 40` | `Below presentation threshold` |
| Salary estimate below floor, high confidence, opt-in enabled | `Below salary floor ({amount})` |
| Timezone band incompatible and the role is not remote | `Timezone incompatible` |
| Employment type not in the Profile's list | `Employment type not sought` |
| Learned preference hard rule (F7) | `Learned preference: {dimension} = {value}` |

Suppressed jobs are **counted and reported** in the digest footer, never silently dropped.

## Related

[[../sad]] §8 · [[../test-plan]] · [[../adr/0001-explainable-linear-scoring|ADR-F4-0001]] ·
[[../../../engineering/security]] §1
