---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "03"
ticket: ""
tags: [sdlc/stage-03, feature/f2-normalization-dedup, mvp, jobhunter]
---

# PRD — f2-normalization-dedup

> **Inputs:** [[../../CONTEXT]] §1 (Job, Fingerprint) · [[../f1-ats-job-discovery/PRD|F1 PRD]] · [[../../00-overview/sad|SAD]] §6.1

## 1. Context

Five providers describe the same thing five different ways. One publishes location as free text
(`"Remote - EMEA"`), one as a structured country/city pair, one as a boolean plus a list. One
publishes salary; four do not. Titles carry seniority inconsistently (`Sr.`, `Senior`, `III`, `Staff`)
and decorate it with noise (`(Remote)`, `- EMEA`, `| Fintech`). Downstream, every stage — the model
prompt, the ranking, the search index, the digest — needs one shape.

Deduplication is the harder half, and the asymmetry matters: **merging two distinct jobs hides one
permanently, while failing to merge two identical jobs shows a duplicate the Owner can ignore in one
tap.** The design is therefore deliberately conservative — an exact fingerprint, and grouping rather
than merging for anything less certain.

The third responsibility is lifecycle. A posting removed from its board is closed, and a closed job
must never appear in a digest or send the Owner to a dead apply URL
([[../../00-overview/idea-brief|brief]] §9).

## 2. Goals

- Produce one canonical vacancy record per real opening, in one shape, regardless of provider.
- Never present the same opening twice in one digest.
- Never hide a distinct opening by merging it into another.
- Know when an opening has closed, and stop showing it.
- Preserve the trail: which raw postings contributed to which canonical job.

## 3. Non-goals

- Judging quality, fit or salary realism — F3 and F4.
- Extracting technologies by reading prose — F3 does that with a model. F2 only matches an explicit
  known-technology vocabulary.
- Cross-company deduplication. Two companies posting the same title are two jobs, always.
- Translating non-English postings. Out of scope for MVP; recorded in [[../../BACKLOG]].

## 4. User stories

### US-01: See one card per opening
**As the** Owner **I want** an opening that appears on several boards to reach me once
**so that** my digest is not padded with the same job three times.

### US-02: Never miss a job because it looked similar to another
**As the** Owner **I want** the platform to merge only when it is certain
**so that** a distinct opening is never silently hidden.

### US-03: Compare jobs on the same terms
**As the** Owner **I want** every job to carry a comparable location, remote policy, seniority and
salary **so that** ranking and filtering mean the same thing across providers.

### US-04: Not be sent to a dead link
**As the** Owner **I want** closed openings excluded **so that** every card I tap leads somewhere.

### US-05: Understand why two postings became one
**As the** operator **I want** the merge trail retained **so that** a suspected bad merge is
answerable from stored data.

### US-06: Benefit from improvements retroactively
**As the** operator **I want** normalisation to be re-runnable over stored raw payloads
**so that** improving a parser improves history, not only the future.

## 5. Acceptance criteria

### AC-01 (US-03) — happy path
**Given** a raw payload from any supported provider
**When** it is normalised
**Then** a canonical vacancy exists carrying title, company, description, apply destination, location
set, remote policy, employment type and — where the provider published one — a structured salary with
its currency and period.

### AC-02 (US-01) — domain invariant
**Given** two raw postings from different sources describing the same opening at the same company
**When** both are normalised
**Then** exactly one canonical vacancy exists, and both raw postings are recorded as contributing to it.

### AC-03 (US-02) — domain invariant
**Given** two openings at the same company with the same title but different locations
**When** both are normalised
**Then** two distinct canonical vacancies exist.

### AC-04 (US-02) — error path
**Given** a raw payload missing a field required to identify an opening
**When** normalisation runs
**Then** no canonical vacancy is created, the reason is recorded against the raw posting, and the
remainder of the batch is unaffected.

### AC-05 (US-03) — happy path
**Given** a title decorated with seniority markers, location suffixes or department noise
**When** it is normalised
**Then** the seniority is extracted into its own comparable field and the decoration is removed from
the comparison form, while the originally published title is preserved unchanged for display.

### AC-06 (US-04) — cross-context
**Given** an opening whose posting has disappeared from every source that carried it
**When** the liveness check runs
**Then** it is marked closed with the time it was last seen, and it is excluded from every subsequent
digest and search result.

### AC-07 (US-04) — cross-context
**Given** an opening that reappears on a board after being marked closed
**When** it is next discovered
**Then** the same canonical vacancy is reopened rather than a second one being created.

### AC-08 (US-05) — domain invariant
**Given** any canonical vacancy
**When** its provenance is inspected
**Then** every raw posting that contributed to it is listed with the source and the time it was seen.

### AC-09 (US-06) — happy path
**Given** stored raw payloads and an improved normalisation rule
**When** reprocessing is requested for a period
**Then** canonical vacancies are recomputed from stored payloads without contacting any provider,
and identities are preserved so downstream references remain valid.

### AC-10 (US-01) — cross-context
**Given** two openings that are similar but not identical enough to merge
**When** the digest is assembled
**Then** they are presented as a group rather than as two unrelated cards, and neither is discarded.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Normalisation throughput | ≥ 500 postings/min single consumer | Messaging benchmark |
| Fingerprint computation | < 1 ms per posting | Unit benchmark |
| False-merge rate | **0** on the labelled corpus | Dedup corpus test |
| False-split rate | ≤ 5% on the labelled corpus | Dedup corpus test |
| Seniority extraction accuracy | ≥ 95% on 200 labelled titles | Fixture test |
| Location parse coverage | ≥ 90% of postings yield ≥1 structured location | Metric |
| Reprocess rate | ≥ 5 000 postings/min, zero network | Benchmark |

## 6.1 Security / privacy

- **Data classification:** public — job content only.
- **Personal data touched:** none extracted. Recruiter names in raw payloads are not carried into `jobs`.
- **AuthZ/AuthN impact:** reprocessing is an operator-scoped operation.
- **Abuse cases:**
  - Hostile HTML in a description → stripped to plain text at the boundary; never rendered as markup.
  - A description crafted to collide fingerprints → the fingerprint uses company, title and location
    only, all of which a hostile poster would have to match legitimately.
  - Reprocess triggered without authorisation → operator scope required.
- **Security review:** N/A — public data, no external calls, no inbound surface.

## 7. Metrics / KPIs

- **Deduplication rate** — target 10–20% of normalised postings merge into an existing job. Far
  below suggests dedup is broken; far above suggests over-merging.
- **False merges reported by the Owner** — target 0. Any occurrence is a defect with a corpus entry.
- **Normalisation failure rate** — target < 1% of raw postings.
- **Closed-job leakage into digests** — target 0.

## 8. Open questions

- [ ] Near-duplicate grouping: trigram similarity, or defer entirely? — owner: Viacheslav —
  *default: `(company, title-trigram ≥ 0.85)` for display grouping only, never merging.*
  ([[../../ARCHITECTURE-OPEN-DECISIONS|O6]])
- [ ] How many consecutive missed cycles before closing a job? — owner: Viacheslav — *default: 2.*
- [ ] Keep full description text indefinitely? — owner: Viacheslav — *default: yes; revisit above 5 GB.*
  ([[../../ARCHITECTURE-OPEN-DECISIONS|O9]])

## DoD self-check

- [x] Coverage types: happy (01, 05, 09), error (04), authorization (via §6.1 reprocess scope), domain invariant (02, 03, 08), cross-context (06, 07, 10)
- [x] No implementation tokens in §5
- [x] Every US has ≥1 AC; NFRs measurable
