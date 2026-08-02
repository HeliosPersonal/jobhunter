---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, feature/f8-company-research-agent, mvp, jobhunter]
---

# Research output contract

> Schema, prompt, citation rules and the fetcher set. The citation rule is the contract; everything
> else supports it.

## Output record

```csharp
public sealed record ResearchOutput(
    string Summary,                       // 2-3 sentences, itself constrained to cited claims
    IReadOnlyList<ClaimDto> Claims);

public sealed record ClaimDto(
    ResearchCategory Category,
    string Claim,                         // one sentence
    string SourceUrl,                     // MUST be one of the URLs supplied in the prompt
    bool IsWarning);
```

```json
{
  "type": "object",
  "required": ["summary", "claims"],
  "properties": {
    "summary": { "type": "string", "maxLength": 500 },
    "claims": {
      "type": "array", "maxItems": 20,
      "items": {
        "type": "object",
        "required": ["category", "claim", "sourceUrl", "isWarning"],
        "properties": {
          "category":  { "enum": ["Funding","EngineeringBlog","OpenSource","Reviews","News","Layoffs","Stack","InterviewProcess"] },
          "claim":     { "type": "string", "maxLength": 300 },
          "sourceUrl": { "type": "string", "format": "uri" },
          "isWarning": { "type": "boolean" }
        }
      }
    }
  }
}
```

The schema can require a `sourceUrl` to be *present*. It cannot require it to be *true*. That is what
the verifier is for.

## Prompt

`JobHunter.Claude/Prompts/ResearchSynthesisPrompt.cs`, `PromptVersion = "research-v1"`.

**System**

```text
You summarise what a set of documents says about a company. You are a summariser, not an expert.

Absolute rules:
- Every claim must be supported by one of the documents provided below. You may not use anything you
  know about this company from any other source. If the documents do not say it, it does not exist.
- Every claim must cite the exact sourceUrl of the document that supports it, copied verbatim from
  the document headers. Do not construct, guess or normalise a URL.
- If the documents are thin, produce few claims. A short honest dossier is correct; a rich one padded
  from memory is a failure.
- Mark isWarning for layoffs, down rounds, funding difficulty, or credible reports of serious
  organisational problems.
- One claim per sentence. State what the source says, not what you infer from it.
- The summary must contain nothing that is not also in a claim.
```

**User** (one item per company)

```text
Company: {displayName} ({canonicalDomain})

--- DOCUMENTS ---
[1] sourceUrl: https://...
    category: Funding
    observed: 2026-08-01
    title: ...
    {extracted text, up to 20000 characters}

[2] sourceUrl: https://...
    ...
--- END DOCUMENTS ---

Categories with no documents found: Reviews, InterviewProcess
```

Listing the empty categories explicitly matters: it tells the model the absence is known and does not
need filling, which measurably reduces the temptation to supply something from memory.

## Citation verification

The rule the whole feature rests on:

```
for each claim:
    if claim.sourceUrl is in { fetched source URLs for this dossier }:
        store the claim, linked to that source
    else:
        discard it, increment claims_discarded, log the fabricated URL
```

Exact match after normalisation of scheme, host case and trailing slash. **Not** fuzzy matching — a
claim citing a URL "close to" a real one is exactly the failure mode being guarded against, and
tolerance here would defeat the purpose.

Discarded claims are counted, never stored. A flagged uncertain claim would still be read as a claim
([[../sad|SAD]] §4 S3).

## Fetcher set

| Category | Source | Target derivation | Allowlist |
|---|---|---|---|
| `EngineeringBlog` | `/blog`, `/engineering`, `/eng`, plus discovered RSS | company domain | the company's own domain only |
| `OpenSource` | GitHub organisation API | org name from the domain, or a link on the site | `api.github.com` |
| `Funding` | public funding feeds and press releases | company name | specific allowlisted hosts |
| `News` | public news RSS search | company name | specific allowlisted hosts |
| `Layoffs` | public layoff trackers with feeds | company name | specific allowlisted hosts |
| `Stack` | the company's own job postings plus its blog | already held, plus the company domain | the company's own domain |
| `Reviews` | only sources with a usable public API or feed | company name | specific allowlisted hosts |
| `InterviewProcess` | the company's own careers and process pages | company domain | the company's own domain |

**Every target is checked twice**: the host must match the category's allowlist, and the resolved
address must be public — checked again after any redirect, because a redirect into private space is
the classic bypass ([[../sad|SAD]] §10 QG-3).

Budget: at most 12 requests and 60 seconds per company, all through F1's politeness handler.

## Cost

One deep-tier item per company. Input is dominated by the fetched text — roughly 15 000 tokens after
truncation — with about 800 output tokens.

`(15000 × 3.00 + 800 × 15.00) / 1e6 × 0.5 ≈ $0.029` per dossier. Five per day is about $0.15,
comfortably inside the Run ceiling alongside enrichment and matching.

## Related

[[../sad]] §6 · [[../test-plan]] · [[../../../CONTEXT]] invariant 5
