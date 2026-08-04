# T13 — CV prompt caching

**Layer:** claude · **Deps:** T04, T12 · **Est:** M · **Owner:** Viacheslav

## What

The caching half of [[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]]: order the matching
prompt so the stable prefix comes first — system prompt, then CV, then the per-job role block — with a
`cache_control` breakpoint at the end of the CV. The prefix is ~2 400 tokens, above the 1 024-token
minimum. This constrains `MatchPrompt`: nothing volatile may precede the breakpoint, so no timestamps,
no run ids, no per-job values in the system prompt.

The cache boundary is carried as structured data, not a magic string. `MatchRequestBuilder` puts the
byte-identical candidate block on `BatchRequestItem.CachePrefix` and the per-job role block on
`UserContent`; the Anthropic request builder splits the user message into a cached prefix block (with
the `cache_control: ephemeral` breakpoint) and the per-item suffix. The fallback (Ollama) and the cost
accountant both read `FullUserContent`, so the split changes neither the tokens billed pessimistically
nor the text a cacheless provider sends.

## Scope note

This task, as originally written, bundled two separable things: the **CV prompt caching wire-up** (the
`cache_control` breakpoint and its falsifiable zero-network assertions, above) and the **regret
sampler + cost/regret metrics + weekly measurement** (a weekly job that matches 20 filtered-out jobs
at cheap tier and alerts on any that would have scored above threshold, plus
`jobhunter.matching.prefiltered` / `jobhunter.matching.regret` and the measured live Run cost). The
sampler and metrics need a Hangfire weekly schedule, the notifier alert path and the live-API cost
measurement — none of which the caching wire-up depends on — so they are split into
[[T21-regret-sampler|T21]] rather than stubbed. The caching mechanism ships here, now, fully asserted
with zero network; T21 proves it empirically once the live suite runs.

## Done when

- The CV and system prompt precede the breakpoint; the per-job block follows it — asserted by the
  submitted-batch structure (one `cache_control` block, on the byte-identical prefix, on every item).
- A 20-item batch round-trip asserts `cache_read_input_tokens > 0` on every item after the first
  (deterministic, zero-network form; the live-API rate is T21).
- A deliberately introduced volatile value before the breakpoint makes the byte-identical-prefix
  assertion fail — proven able to catch a regression.
- Items sharing a CV version are submitted as one batch; splitting them is not possible through the
  public API of the submitter (the builder emits one batch per `Build`).

## Done when (moved to [[T21-regret-sampler|T21]])

- The regret sampler runs weekly, samples 20 excluded jobs, and alerts on any that would have exceeded
  the threshold.
- `jobhunter.matching.prefiltered` and `jobhunter.matching.regret` are exported and charted.
- Measured Run cost after both changes is ≈ $1.03 at 150 jobs/day, recorded in
  [[../../../operations/infrastructure|infrastructure]] §8.

## Links

[[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]] · [[../contracts/match-schema]] ·
[[T21-regret-sampler|T21]] · [[../../../operations/infrastructure]] §8
