# T08 — Outcome signals

**Layer:** app · **Deps:** T03 · **Est:** M · **Owner:** Viacheslav

## What

Publish weighted `signals` rows on terminal outcomes. An interview survived a real
filter; a tap survived two seconds of attention — so the weights differ by a factor of four, and F7
consumes them accordingly.

## Done when

- Reaching `Applied`, `Interview`, `Offer` or `Rejected` produces a signal with the documented weight (AC-08).
- The signal captures the job's facts at that moment, so a later edit cannot rewrite history.
- The signal is written in the same transaction as the transition.
- Weights are configuration, documented in [[../sad|SAD]] §8, not hard-coded.
- A repeated transition to the same status does not produce a duplicate signal.

## Links

[[../../f7-preference-learning/index|F7]] · [[../sad]] §8
