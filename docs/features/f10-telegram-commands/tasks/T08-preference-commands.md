# T08 — Profile and preference commands

**Layer:** telegram · **Deps:** T05 · **Est:** M · **Owner:** Viacheslav

## What

`/cv`, `/prefs`, `/forget`, `/floor`. `/prefs` is the chat face of F7's explainability
contract — every weight as one sentence with its evidence. **`/cv` shows metadata only**; the F4
boundary holds here.

## Done when

- `/cv` shows version, activation date and current match count, and **no CV content whatsoever** — asserted by the F4 sentinel scan extended to cover this path. It is **read-only**: it never uploads a CV; that path is F4's, outside the command surface.
- `/prefs` renders each weight as one sentence quoting its rate and count; below 200 signals it says how many more are needed.
- `/forget` disables a weight and states that it takes effect on the next ranking, not mid-Run (AC-05).
- `/floor` previews how many of today's jobs the change would have affected, before making it.
- Explicit floor overrides any learned salary weight — asserted against F7.

## Links

[[../contracts/command-catalogue|catalogue]] §Profile and preferences · [[../../f7-preference-learning/index|F7]] · [[../../f4-cv-matching-ranking/contracts/match-schema|F4 CV rules]]
