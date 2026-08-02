# T12 — Rendering corpus and live smoke checklist

**Layer:** tests · **Deps:** T09, T11 · **Est:** M · **Owner:** Viacheslav

## What

The ~200-case snapshot corpus over a fake notifier, covering every layout, every degraded
variant, every hostile input and every splitting boundary. Plus the manual pre-release checklist for
one real message to a test chat — some things only break in the real client.

## Done when

- Every layout in the contract has a committed snapshot; a layout change is visible in the PR diff.
- Every row of the hostile-input table renders safely with the layout intact.
- Splitting never occurs mid-card, asserted just under, at and just over 4096 characters.
- The whole corpus runs in under 10 s so it is never the reason tests are skipped.
- The live-smoke checklist exists and has been executed once against a real chat.
- The four action buttons are verified working in a real Telegram client before M4 is called done.

## Links

[[../test-plan]] §The rendering corpus
