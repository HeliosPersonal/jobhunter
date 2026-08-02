# T13 — Crash matrix, golden set and cost dashboards

**Layer:** tests · **Deps:** T12 · **Est:** L · **Owner:** Viacheslav

## What

The two suites the feature's credibility rests on: the eight-checkpoint crash matrix
(QG-1) and the 50-job golden set with expected **bands** rather than exact values. Plus the cost and
pipeline Grafana panels and the four alerts from
[[../../../engineering/observability|observability]] §4.

## Done when

- All eight crash checkpoints pass, each asserting `SubmitAsync` was invoked exactly once.
- Ledger totals after an interrupted-and-resumed Run equal those of an uninterrupted one.
- The golden set asserts bands, not exact values, so a non-deterministic model does not produce a flaky test.
- Cost, run-state and batch-latency panels exist and are populated from a real staging Run.
- Alerts fire in staging when deliberately provoked — an alert that has never fired is an alert nobody has verified.
- The nightly live-drift job runs, compares against fixtures and alerts without gating any build.

## Links

[[../test-plan]] · [[../../../engineering/observability]] §4
