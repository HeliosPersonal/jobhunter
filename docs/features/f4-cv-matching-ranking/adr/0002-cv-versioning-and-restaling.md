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

# F4-0002 — Immutable CV versions; stale old matches rather than rewrite them

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The CV changes — a new role, a new skill, a rewrite for a different kind of target. Every match ever
computed was computed against a particular version of it. We must decide what a change means for
those existing matches, and what happens to the CV that produced them. Resolves
[[../../../ARCHITECTURE-OPEN-DECISIONS|O10]].

## Decision drivers

- A match presented today must reflect today's CV, or it is misleading in exactly the way that erodes
  trust in the whole digest.
- Re-matching everything is expensive; re-matching nothing is dishonest.
- Yesterday's digest said what it said, and being able to explain it later is worth something.
- The CV is the system's only personal data; less of it stored is better than more.

## Considered options

1. **One mutable CV row** — overwrite on upload; existing matches unchanged.
2. **Versioned CVs; existing matches left alone**, silently outdated.
3. **Versioned CVs; existing matches marked stale; recent live jobs re-matched.**
4. **Versioned CVs; re-match everything on every change.**

## Decision outcome

**Chosen: Option 3.**

- `cv_versions` rows are **immutable**. An upload inserts a new version and deactivates the previous
  one. Content-hash equality makes re-uploading the same file a no-op rather than a spurious version.
- On activation, every `is_current` match computed against an older version is set `is_current = false`.
  They are **marked, not deleted** — they remain the honest record of what was true when yesterday's
  digest was produced.
- Live jobs first seen in the **last 30 days** are queued for re-match on the next Run, at **cheap
  tier**. Thirty days is where a job's realistic chance of still being open meets the cost of
  re-matching; the cheap tier is used because re-matching is a correction, not a first judgement.
- The uploaded **binary is not retained**. Text is extracted once, in-process, at upload, and the file
  is discarded — less personal data at rest, and no file to serve or leak.

Option 1 destroys history and makes "why did the digest say that" unanswerable. Option 2 presents
stale judgements as current, which is the specific dishonesty this feature exists to avoid. Option 4
costs a full deep-tier re-run of the entire corpus for a change that mostly affects recent jobs.

## Consequences

**Positive**
- The digest never shows a match computed against a CV the Owner no longer has.
- History is preserved and explainable — an old match with `is_current = false` is a fact about the past.
- Cost of a CV change is bounded and predictable, and is ledgered like any other work so the Run
  ceiling governs it.
- Only extracted text is stored, which narrows the personal-data surface to one column.

**Negative**
- Jobs older than 30 days keep matches from the previous CV until they close. They are marked stale
  and rank lower by freshness anyway, so the practical exposure is small.
- A CV change on a busy day produces a cost spike. Bounded by the ceiling, which will abort rather
  than overspend — the correct behaviour.

**Neutral**
- The version number is user-visible, which makes "this was matched against CV v3" a sentence the
  digest could say if it ever needs to.

## Links

- [[../PRD]] AC-08 · [[../data-model]] §cv_versions · [[../../../ARCHITECTURE-OPEN-DECISIONS|O10]]
- [[../../../engineering/security]] §1
