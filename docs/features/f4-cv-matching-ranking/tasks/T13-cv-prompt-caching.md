# T13 — CV prompt caching and regret sampler

**Layer:** claude/app · **Deps:** T04, T12 · **Est:** M · **Owner:** Viacheslav

## What

Two things that together make [[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]] safe and
cheap.

**Caching.** Order the matching prompt so the stable prefix comes first — system prompt, then CV,
then the per-job role block — with a `cache_control` breakpoint at the end of the CV. The prefix is
~2 400 tokens, above the 1 024-token minimum. This constrains `MatchPrompt`: nothing volatile may
precede the breakpoint, so no timestamps, no run ids, no per-job values in the system prompt.

**Regret sampler.** A weekly job that takes 20 jobs the filter excluded, matches them at cheap tier,
and alerts if any would have scored above the presentation threshold. This is the control that
catches a wrong filter rule; without it the filter is unfalsifiable.

## Done when

- The CV and system prompt precede the breakpoint; the per-job block follows it — asserted by a snapshot of the rendered prompt.
- An integration test over a 20-item batch asserts `cache_read_input_tokens > 0` on every item after the first.
- A deliberately introduced volatile value before the breakpoint makes that test fail — the assertion is proven able to catch a regression.
- Items sharing a CV version are submitted as one batch; splitting them is not possible through the public API of the submitter.
- The regret sampler runs weekly, samples 20 excluded jobs, and alerts on any that would have exceeded the threshold.
- `jobhunter.matching.prefiltered` and `jobhunter.matching.regret` are exported and charted.
- Measured Run cost after both changes is ≈ $1.03 at 150 jobs/day, recorded in [[../../../operations/infrastructure|infrastructure]] §8.

## Links

[[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]] · [[../contracts/match-schema]] · [[../../../operations/infrastructure]] §8
