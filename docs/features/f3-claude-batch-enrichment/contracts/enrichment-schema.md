---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, feature/f3-claude-batch-enrichment, mvp, jobhunter]
---

# Enrichment output contract

> The schema, the prompt and the parsing rules. Changing anything here bumps `PromptVersion` and
> requires updated golden fixtures ([[../../../IMPLEMENTATION-READINESS|gate G10]]).

## Output record

The C# record is the source of truth; the JSON Schema is generated from it, so the two cannot drift.

> **Planned change (TUNE-03, F3 T15):** add a `RoleFamily` enum
> (`AiPlatform | Platform | AiApplications | ForwardDeployed | FoundingEng | BackendGeneric | Frontend |
> Fullstack | DevOpsSRE | MlResearch | DataScience | PromptEng | EnterpriseCrud | Other`), classified by
> the *work described* rather than the title, with a reason. See the
> [[../../../reviews/career-alignment-tuning-backlog|tuning backlog]].
>
> **Planned change (TUNE-04, F3 T16):** add AiUsage sub-signals (e.g. `buildsAiProduct`, `buildsAiInfra`,
> `usesAiTooling`, `isResearch`) alongside the existing scalar to sharpen the target/trap boundary.

```csharp
public sealed record EnrichmentOutput(
    SalaryEstimateDto? Salary,              // null when the model genuinely cannot tell
    bool IsRemote,
    bool IsContractorFriendly,
    TimezoneBand TimezoneBand,              // EMEA | AMER | APAC | Global | Unknown
    AiUsageLevel AiUsage,                   // None | Low | Medium | High
    CompanyStage CompanyStage,              // Seed | SeriesA..SeriesD | Public | Bootstrapped | Unknown
    IReadOnlyList<string> Technologies,     // canonical names where recognised
    IReadOnlyList<string> Reasons);         // >= 1, else the item is rejected

public sealed record SalaryEstimateDto(
    decimal Min, decimal Max, string Currency, SalaryPeriod Period, decimal Confidence);
```

Generated schema (abridged):

```json
{
  "type": "object",
  "required": ["isRemote", "isContractorFriendly", "timezoneBand", "aiUsage", "companyStage", "technologies", "reasons"],
  "properties": {
    "salary": {
      "type": ["object", "null"],
      "required": ["min", "max", "currency", "period", "confidence"],
      "properties": {
        "min":        { "type": "number", "minimum": 0 },
        "max":        { "type": "number", "minimum": 0 },
        "currency":   { "type": "string", "pattern": "^[A-Z]{3}$" },
        "period":     { "enum": ["Year", "Month", "Day", "Hour"] },
        "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
      }
    },
    "isRemote":              { "type": "boolean" },
    "isContractorFriendly":  { "type": "boolean" },
    "timezoneBand":          { "enum": ["EMEA", "AMER", "APAC", "Global", "Unknown"] },
    "aiUsage":               { "enum": ["None", "Low", "Medium", "High"] },
    "companyStage":          { "enum": ["Seed","SeriesA","SeriesB","SeriesC","SeriesD","Public","Bootstrapped","Unknown"] },
    "technologies":          { "type": "array", "items": { "type": "string" }, "maxItems": 25 },
    "reasons":               { "type": "array", "items": { "type": "string" }, "minItems": 1, "maxItems": 6 }
  }
}
```

`"minItems": 1` on `reasons` encodes [[../../../CONTEXT]] invariant 4 at the schema level, so the
provider constrains generation rather than us rejecting afterwards. We still validate — belt and
braces — but the first line of defence is the schema.

## Prompt

`JobHunter.Claude/Prompts/EnrichmentPrompt.cs`, `PromptVersion = "enrich-v1"`.

**System**

```text
You assess software engineering job postings. You extract facts and make calibrated estimates about
the ROLE — never about any particular candidate.

Rules:
- Base every field on the posting text. Do not invent a salary a posting does not support.
- "Remote" means the role can be performed remotely long-term, not "remote during onboarding" and
  not "remote within 50km of the office".
- "Contractor friendly" requires positive evidence: B2B, contract, freelance, consultant, or an
  explicit statement. Silence means false.
- Timezone band is where the role expects overlap, which is often not where the company is.
- AI usage is how much the ENGINEERING work involves building with or on AI systems. A company that
  sells an AI product but whose posting describes CRUD work is Low.
- Company stage: only from evidence in the posting (funding mentions, size statements, "public
  company", "early stage"). Otherwise Unknown.
- Every reason must be specific and quote or paraphrase the posting. "Good role" is not a reason.
- If you cannot tell, say Unknown or null. A confident wrong answer is worse than an honest gap.
```

**User** (per item)

```text
Company: {companyName} ({canonicalDomain})
Title: {title}
Location: {locationSummary}
Published salary: {publishedSalaryOrNone}
Employment type: {employmentType}

--- POSTING ---
{description, truncated to 12000 characters at a paragraph boundary}
--- END POSTING ---
```

Truncation is at a paragraph boundary, never mid-sentence, and the truncation is recorded on the
batch item so a suspiciously poor assessment can be checked against it.

## Parsing rules

Applied in order; the first failure records the item and stops:

| Step | Rule | On failure |
|---|---|---|
| 1 | Provider reported an error for the item | `ProviderError`, retry once next Run |
| 2 | JSON parses | `ParseFailed`, raw retained |
| 3 | Validates against the schema | `ParseFailed` with the failing path |
| 4 | `reasons` is non-empty after trimming blanks | `ParseFailed` — invariant 4 |
| 5 | Salary present ⇒ `max >= min`, currency is real ISO-4217 | swap if inverted; drop salary if the currency is unknown, keep the rest |
| 6 | `confidence` in `[0,1]` | clamp |
| 7 | Technologies mapped to canonical names | unknown names retained verbatim, capped at 25 |
| 8 | Enum values recognised | unknown value → `Unknown`, logged; **never throw** |

Step 8 matters more than it looks: a provider adding an enum value must degrade to `Unknown`, not
crash the poller at 03:00.

## Cost model

`PricingTable` in configuration, USD per million tokens:

| Tier | Model id (configured) | Input | Output | Batch discount |
|---|---|---|---|---|
| `Cheap` | `claude-haiku-4-5` | 1.00 | 5.00 | 50% |
| `Deep` | `claude-sonnet-5` | 3.00 | 15.00 | 50% |

`Deep` may be raised to `claude-opus-5` ($5.00 / $25.00) — a configuration change, not a code change
([[../../../00-overview/adr/0005-anthropic-message-batches-two-tier-cascade|ADR-0005]]). Verified
against Anthropic list pricing on 2026-08-02; **`PricingTable` is the single place these numbers
live, and a stale table silently invalidates the ceiling** — the estimate-vs-actual metric is what
detects that (SAD §11 D3).

> Sonnet 5 carries introductory pricing of $2.00 / $10.00 through **2026-08-31**. Plan against the
> standard rate; treat the discount as headroom, not budget.

Estimation counts tokens from the **actually rendered prompt**, not from a per-job heuristic, which
is why the estimate lands within 20% rather than within a factor. Output tokens are estimated from
the schema's maximum plausible size — deliberately pessimistic, so the ceiling errs toward
under-spending.

Enrichment at 150 jobs: ~4 000 input + ~350 output tokens each →
`150 × (4000 × 1.00 + 350 × 5.00) / 1e6 × 0.5 ≈ $0.43`.

Whole-Run budget at 150 jobs/day, for context — full breakdown in
[[../../../operations/infrastructure|infrastructure]] §8:

| Stage | Tier | Items | $/Run |
|---|---|---|---|
| Enrichment | Cheap | 150 | $0.43 |
| Matching ([[../../f4-cv-matching-ranking/index\|F4]]) | Deep | 150 | $1.58 |
| Synthesis ([[../../f5-daily-digest-telegram/index\|F5]]) | Deep | 1 | $0.01 |
| Research ([[../../f8-company-research-agent/index\|F8]]) | Deep | 5 | $0.14 |
| **Total** | | | **$2.16** |

That naïve total of **$2.16 is over the $2.00 ceiling** and would be **aborted pre-submission** — the
ceiling is a precondition, not an alarm. That is why [[../../f4-cv-matching-ranking/index|F4]] applies
a pre-match filter and caches the shared CV prefix
([[../../f4-cv-matching-ranking/adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]]), which brings
matching down from a naïve $1.58 to ≈$0.44. With both, a Run costs ≈ $1.03, comfortably under the
$2.00 ceiling rather than over it.

## Versioning

`PromptVersion` changes whenever the system prompt, the user template, the schema or a parsing rule
changes. It is stamped on `batches.prompt_version` and `enrichments.prompt_version`, which is what
lets a quality regression be attributed to a specific change (AC-11) rather than argued about.

## Related

[[../sad]] §8 · [[../test-plan]] · [[../../../00-overview/adr/0006-structured-output-contract|ADR-0006]]
