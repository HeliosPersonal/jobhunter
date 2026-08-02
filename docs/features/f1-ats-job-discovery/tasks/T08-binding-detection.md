# T08 — ATS binding detection

**Layer:** app · **Deps:** T06 · **Est:** L · **Owner:** Viacheslav

## What

`AtsProbeDetector`: derive candidate board tokens from the canonical domain and any
careers URL, probe each provider, score the evidence per
[[../contracts/ats-endpoints|the detection table]], and record the full probe trail. Never guess —
a binding requires a successful fetch as evidence.

## Done when

- A single scoring candidate produces a binding with confidence ≥ 0.80 and recorded evidence (AC-03).
- No candidate produces `NoBoardFound` with the probes attempted, not an exception (AC-03).
- Two candidates ≥ 0.80 produce `Ambiguous` with all candidates; the company stays inactive (AC-04).
- Accuracy ≥ 95% on the 50-company labelled set — the test fails below 47.
- Probes respect the rate budget like any other fetch (they go through T04).
- Evidence is rich enough to explain a wrong binding without re-running detection.

## Links

[[../contracts/ats-endpoints|ATS endpoints]] §Detection probes · [[../test-plan]] §Detection
