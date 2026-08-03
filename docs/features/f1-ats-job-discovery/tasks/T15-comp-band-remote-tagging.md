# T15 — Tag companies by comp band and remote-from-EMEA posture

**Layer:** app · **Deps:** T03 · **Est:** M · **Owner:** Viacheslav

## What

For a $180k–$400k remote-from-Kyiv goal the company universe should be comp-and-remote-segmented; today
`companies.yaml` is a flat list that mixes US high-comp firms with lower-band GB/EU ones, with no signal
to bias discovery or the digest. Add two optional fields to the seed schema and use them to bias
acquisition and digest ordering toward the target band.

Proposed schema additions (both optional, so existing rows stay valid):

- `comp_band` — a coarse band label (e.g. `Top`, `High`, `Mid`) capturing the employer's typical comp
  posture for senior/staff engineering.
- `remote_emea_friendly` — a boolean for whether the employer hires remote from EMEA / Ukraine.

## Done when

- `CompanySeedLoader` accepts `comp_band` and `remote_emea_friendly` as optional fields; rows omitting
  them still load (backward compatible), and a malformed value fails the command naming the line.
- The two fields are persisted on the company registry row (migration + read model as needed).
- Discovery / digest prioritisation biases toward the target comp band and feeds `remote_emea_friendly`
  into acquisition prioritisation — reason-visible, not a silent hard filter.
- A test asserts a `remote_emea_friendly` / target-band company is prioritised over an equivalent
  non-matching one, and that untagged rows keep working (no regression).

## Links

[[../../../reviews/career-alignment-tuning-backlog|career-alignment tuning backlog]] TUNE-10 ·
`tools/seed/companies.yaml` · [[T03-registry-seeding]] · [[T14-ai-devtools-company-universe]]
