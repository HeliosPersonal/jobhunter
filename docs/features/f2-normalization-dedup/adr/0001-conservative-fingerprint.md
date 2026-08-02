---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f2-normalization-dedup, jobhunter]
---

# F2-0001 — Conservative exact fingerprint; group, never merge, on uncertainty

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The same opening reaches us from several boards, and the same recruiter reposts the same role every
fortnight. We must decide what makes two postings "the same job" and — more importantly — what to do
when we are not sure. Resolves [[../../../ARCHITECTURE-OPEN-DECISIONS|O6]].

The two failure modes are not symmetric:

- **False split** — one opening becomes two jobs. The Owner sees a duplicate card and taps *Ignore*.
  Cost: one tap and a slightly padded digest.
- **False merge** — two openings become one job. One is now invisible, permanently, and the Owner
  never learns it existed. Cost: an opportunity, silently.

Everything below follows from that asymmetry.

## Decision drivers

- A false merge is unrecoverable and undetectable from the Owner's side.
- A false split is visible, cheap and self-correcting through the Ignore action.
- The decision runs on every normalised posting and must be fast and deterministic.
- It must be explainable — "why did these become one job" needs an answer from stored data ([[../PRD]] AC-08).
- Fuzzy matching invites a threshold; tuning a merge threshold means tuning the rate at which jobs
  silently disappear.

## Considered options

1. **Exact match on (company, published title, location set)** — no normalisation at all.
2. **Exact match on (company, normalised title, normalised location set)** — normalise first, then match exactly.
3. **Fuzzy similarity** — trigram or embedding similarity over title and description, merge above a threshold.
4. **Model-assisted** — ask Claude whether two postings are the same job.

## Decision outcome

**Chosen: Option 2**, with near-duplicates *grouped for display* but never merged.

The fingerprint is `sha256` over the canonical domain, the normalised title and the sorted location
key set, each separated by the unit separator byte so that a title ending in the characters a
location begins with cannot forge a collision.

Normalisation is limited to transformations that are **certainly** meaning-preserving: case folding,
whitespace collapsing, removal of bracketed and post-separator decoration, and canonicalisation of
seniority abbreviations. Anything that *might* carry meaning — a team name, a specialisation, a level
number — is left in and therefore distinguishes.

Uniqueness is enforced by a unique index rather than by application logic, so two concurrent
consumers racing on one opening produce exactly one row with no lock.

**Near-duplicates** (same company, trigram similarity ≥ 0.85 on the normalised title, same location
set) are computed at digest assembly and rendered as a group. They remain two jobs with two
identities, two enrichments and two matches. Grouping is a presentation decision and is reversible;
merging is a data decision and is not.

Option 3 is rejected because the threshold is the problem, not the technique: every threshold that
merges the true duplicates also merges some true distincts, and we have no way to detect which ones
it got wrong. Option 4 adds cost and latency to a decision that must be deterministic and instant,
and replaces a rule we can inspect with a judgement we cannot.

## Consequences

**Positive**
- Zero false merges by construction on the labelled corpus — the property the design exists to have.
- Deterministic, sub-millisecond, reproducible across machines and cultures.
- Explainable: `job_aliases` lists exactly which postings contributed, with sources and times.
- Concurrency-safe with no application-level coordination.

**Negative**
- Some genuine duplicates escape as separate jobs, chiefly where two boards describe a location
  differently. Location normalisation reduces this and display grouping hides most of the remainder.
  Budgeted at ≤ 5% false-split.
- Changing the algorithm invalidates every stored fingerprint. Mitigated by `fingerprint_version` on
  each job, making a change an explicit migration with a re-fingerprint step rather than a silent
  redefinition.

**Neutral**
- The corpus is the specification. A disputed pair is settled by adding it with a label, which makes
  the rule empirical rather than argued.

## Links

- [[../PRD]] §5 AC-02, AC-03, AC-10 · [[../sad]] §10 QG-1 · [[../test-plan]] §The dedup corpus
- [[../../../CONTEXT]] invariant 2 · [[../../../ARCHITECTURE-OPEN-DECISIONS|O6]]
