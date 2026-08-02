# T07 — Technology vocabulary tagging

**Layer:** app · **Deps:** T04 · **Est:** S · **Owner:** Viacheslav

## What

`TechnologyTagger`: match a curated vocabulary of ~300 canonical technologies and their
aliases against title and description, writing `job_technologies` with how each match was made.
Vocabulary matching only — no inference, no model. F3 writes inferred technologies elsewhere, so the
deterministic set stays separable.

## Done when

- All spellings of a technology map to one canonical name.
- Word-boundary matching only — a two-letter language name does not match inside a longer word.
- `matched_via` records whether the hit was in the title or the description, so title matches can be weighted later.
- The vocabulary is a committed YAML file, reviewable in a diff.
- Tagging never writes to the enrichment table owned by F3.

## Links

[[../data-model]] §job_technologies
