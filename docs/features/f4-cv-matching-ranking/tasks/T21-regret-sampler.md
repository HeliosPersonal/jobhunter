# T21 — Regret sampler, matching metrics and live cost measurement

**Layer:** app · **Deps:** T12, T13, F5 · **Est:** M · **Owner:** Viacheslav

## What

The measurement half of [[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]], split from
[[T13-cv-prompt-caching|T13]] because it needs infrastructure the caching wire-up does not.

**Regret sampler.** A weekly Hangfire job takes 20 jobs the pre-match filter excluded, matches them at
cheap tier, and alerts (via the notifier) if any would have scored above the presentation threshold. A
non-zero regret rate means a filter rule is wrong. This is the control that makes the filter
falsifiable — without it a wrong rule silently removes jobs the Owner would have wanted, which is the
one real risk the ADR names.

**Metrics.** `jobhunter.matching.prefiltered` (how many jobs the filter excluded, by rule) and
`jobhunter.matching.regret` (how many sampled excluded jobs would have scored above threshold) are
exported through the Application telemetry instruments and charted.

**Live cost measurement.** The opt-in weekly live-API suite confirms the empirical cache-hit rate and
the measured Run cost (≈ $1.03 at 150 jobs/day), so the $1.03 figure in
[[../../../operations/infrastructure|infrastructure]] §8 is verified rather than asserted.

## Done when

- A weekly job samples 20 filter-excluded jobs, matches them at cheap tier, and alerts on any that
  would have exceeded the presentation threshold. The job is idempotent per week.
- `jobhunter.matching.prefiltered` and `jobhunter.matching.regret` are exported and charted.
- The measured Run cost (≈ $1.03 at 150 jobs/day) and the empirical cache-hit rate are recorded from
  the live suite in [[../../../operations/infrastructure|infrastructure]] §8.

## Blocked on

- **A weekly schedule and the notifier alert path** — the sampler is a Hangfire recurring job that
  raises an alert; it reuses the F5 notifier surface.
- **The live-API suite** — the cost/cache-hit measurement is an opt-in weekly run
  ([[../../../engineering/testing-strategy|testing-strategy]] §Live API tests), not a PR-suite test.

## Links

[[../adr/0003-pre-match-filter-and-cv-caching|ADR-F4-0003]] · [[T12-pre-match-filter|T12]] ·
[[T13-cv-prompt-caching|T13]] · [[../../../operations/infrastructure]] §8
