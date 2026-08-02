# T10 — Callback handling, actions and Signal capture

**Layer:** telegram · **Deps:** T08 · **Est:** L · **Owner:** Viacheslav

## What

`CallbackHandler`: resolve the signed short id, apply the action, capture a `Signal` with
the job's facts **at that moment**, acknowledge within a second and update the keyboard. The action and
the Signal commit in one transaction — capture must not be a separate step that can fail
independently.

## Done when

- All four actions are recorded, acknowledged and reflected in the keyboard (AC-03).
- Acknowledgement happens in under one second, asserted with a ceiling (QG-3).
- A Signal is captured in the same transaction, carrying the job's facts at that moment (AC-08).
- A tap on a closed or missing job produces a plain message and records nothing invalid (AC-09).
- A forged or unresolvable short id produces a clear message, never a silent no-op.
- Tapping the same button twice is idempotent and re-acknowledges.
- The Ignore acknowledgement reads `Won't show similar` — the phrasing is part of the contract ([[../../../DECISION-LOG|D7]]).

## Links

[[../contracts/telegram-messages]] §Callback payloads · [[../../f7-preference-learning/index|F7]]
