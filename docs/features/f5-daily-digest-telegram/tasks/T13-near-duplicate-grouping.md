# T13 — Near-duplicate grouping at digest assembly

**Layer:** app · **Deps:** T03 · **Est:** S · **Owner:** Viacheslav

## What

`NearDuplicateGrouper`, applied during digest assembly. Two jobs that are the *same real opening*
posted under slightly different titles or on two boards are grouped into one card for display, with
the others reachable, so the digest never shows the Owner the same role twice. Relocated here from F2
because grouping is a **presentation** concern computed at digest assembly, not a canonicalisation
concern ([[../../f2-normalization-dedup/adr/0001-conservative-fingerprint|ADR-F2-0001]] — "computed
at digest assembly"). F2 owns canonical `Job` identity; F5 owns how near-duplicates are *shown*.

## Done when

- Near-duplicate jobs are grouped into one presented card during assembly; the grouped-away jobs
  remain queryable and are not lost (the F2 dedup grouping property, now realised at display time).
- Grouping runs on the assembled card set, after selection and before persistence, so the grouping is
  snapshotted onto the digest like everything else and a replay reproduces it.
- Grouping is deterministic — the same card set always groups the same way and picks the same
  representative card.
- The grouper reads only the assembled cards and their jobs; it does not re-open the dedup pipeline.
- The footer/counters reflect grouped cards, so the "N shown" count matches what is actually rendered.

## Implementation map

> Mechanical checklist. Copy the named exemplars; contested points resolved here per the docs.

**Where it slots in (fixed).** Grouping runs **on the assembled card set, after `SelectCandidates` and
before persistence**, inside `DigestAssembler.Handle` — so the grouping is snapshotted onto the digest
like everything else and a replay reproduces it. It reads **only the assembled cards and their jobs**; it
does **not** re-open the F2 dedup pipeline (F2 owns canonical `Job` identity; F5 owns how
near-duplicates are *shown* — ADR-F2-0001 "computed at digest assembly").

**Files to create**
- `src/JobHunter.Application/Reporting/NearDuplicateGrouper.cs` — pure, deterministic. Input: the ordered
  `List<DigestCard>` (already score-sorted). Output: representative cards + the grouped-away card ids
  attached to their representative. **Determinism (fixed criterion):** same card set → same grouping →
  same representative (pick the highest-scored, ties broken by job id — the query already orders this way).
- If grouping metadata must survive on the aggregate: add a `GroupedJobIds` (or a `DigestCardGroup`
  child) to `src/JobHunter.Domain/Reporting/DigestCard.cs` so grouped-away jobs remain **queryable and
  are not lost** (the F2 grouping property, realised at display time). Prefer the minimal field over a new
  table unless the data model already specifies one — check `data-model.md` first.

**Files to edit**
- `src/JobHunter.Application/Reporting/DigestAssembler.cs` — call `NearDuplicateGrouper.Group(selected)`
  between `SelectCandidates` and card construction (lines ~99–107). Re-rank after grouping so ranks are
  contiguous on the *representative* set. **Counters/footer must reflect grouped cards** — the "N shown"
  count matches what is actually rendered, so `cards.Count`/`StrongMatches` count representatives.

**Grouping rule (conservative, per ADR-F2-0001).** Two cards are the *same real opening* when they share
a conservative fingerprint — same normalised title + same company, or the F2 near-duplicate signal if one
is already computed on the job. **When in doubt, do not group** (a false merge hides a real role — the F2
"zero false merges" property applies here too). Reuse any existing normaliser in
`JobHunter.Application/Normalization/` rather than inventing a new title normaliser.

**Tests** (`tests/JobHunter.Application.Tests/Reporting/NearDuplicateGrouperTests.cs`)
- Near-duplicates group into one presented card; grouped-away jobs remain queryable (assert they're on
  the representative, not dropped).
- Determinism: the same card set groups the same way and picks the same representative across runs.
- Distinct roles are **not** merged (the conservative floor — mirror F2's dedup-corpus discipline).
- Counters reflect grouped cards: extend `DigestAssemblerTests.cs` to assert "N shown" == representatives.
- Replay reproduces the grouping (it's snapshotted on the persisted digest).

**Est is S** — this is a pure function + one assembler splice + a domain field. Do not over-build; no new
pipeline stage, no message, no F2 change.

## As built

- **Grouping key: `(CompanyId, NormalisedTitle)`.** Both halves required and non-blank; a candidate
  missing either stands alone. This is coarser than F2's `Fingerprint` (a SHA-256 over canonical domain,
  normalised title and sorted locations — unique per job, so identical fingerprints never co-occur) and
  deliberately so: two boards can post one opening under different location strings, which the fingerprint
  splits but a human reads as one card. The title is trimmed and lowered with `ToLowerInvariant`; the
  existing `normalised_title` column is reused, so no new normaliser was invented.
- **Slot: `DigestAssembler.AssembleForRunAsync`, on the *selected* set, after `SelectCandidates` and
  before verification and persistence.** Grouping runs before apply-link verification so only
  representatives are probed, and the grouping is snapshotted onto the persisted digest — a replayed
  `RankingCompleted` finds the committed digest and re-emits it, reproducing the grouping.
- **Data threading.** Neither read model carried company/title, so `CompanyId`/`NormalisedTitle` were
  added to `DigestCandidate` (projected in `DigestScopeQuery` from `jobs.company_id`/`normalised_title`,
  Dapper read-only) and `GroupedJobIds` to `DigestCard`, persisted as a `jsonb` array
  (`digest_cards.grouped_job_ids`, migration `F5AddGroupedJobIds`, serialised via `GuidListJson`). Both
  new fields are optional constructor parameters, so no existing call site changed.
- **`StrongMatches` intentionally left counting *all* strong scores, not representatives.** The map
  suggested representatives, but a deliberate T03 test (`Strong_matches_counts_every_shown_score...`)
  asserts the header counts every score at or above the threshold beyond the ten-card cap — grouping is a
  *display* concern and does not change how many strong matches the day actually produced. The binding
  "Done when" is that the **shown count** reflects grouping, and it does: `cards.Count` is the
  representative count once grouped-away cards leave the list. `StrongMatches` was left untouched.

## Links

[[../sad]] §6.1 · [[../../f2-normalization-dedup/adr/0001-conservative-fingerprint|ADR-F2-0001]] ·
[[T03-digest-assembler|T03]]
