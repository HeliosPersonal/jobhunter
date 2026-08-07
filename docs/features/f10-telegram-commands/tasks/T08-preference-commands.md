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

## Implementation

Four commands over F7's explainability contract and the F4 boundary, each behaviour owned by the
feature it belongs to; F10 adds the Telegram-facing rendering and, for `/floor`, the preview-before-write
discipline the catalogue mandates.

**`/cv`.** Reads version, activation date and current match count through `ICvStatusQuery` and renders
them, and **nothing else** — no title, no snippet, no bytes of the document. The F4 sentinel scan is
extended to cover this path, so a regression that leaked any CV content would fail the build, not merely
review. It is **read-only**: uploading a CV is F4's path, outside the command surface.

**`/prefs`.** The chat face of F7's explainability contract (AC-03): each active weight is rendered as
one sentence quoting its positive rate and evidence count through F7's shared sentence renderer over
`ActiveWeightsQuery`, so the wording is identical to the API's and never a second copy. Below the 200-signal
learning floor it says how many more signals are needed rather than showing a half-trained model as if it
were settled (`IPreferenceStatusQuery`).

**`/forget`.** Disables a named dimension's weight(s) through `DisablePreferenceWeightHandler` — the one
write path — and states that it takes effect on the **next ranking, not mid-Run** (AC-05). An unknown or
absent dimension is answered with the forgettable pick-list rather than an error, and with nothing learned
yet it says so plainly.

**`/floor` — previewed before it is made.** The catalogue mandates the change be previewed: the reply
states how many of today's shown jobs the floor *would have* affected before anything is written.
That count comes from `ISalaryFloorPreviewQuery`, whose SQL is exactly the `SuppressionEvaluator`
below-floor rule — same currency (never cross-currency), high confidence (≥ 0.7), estimate wholly below
the floor — over the latest Run's non-suppressed scores, so the preview cannot drift from what suppression
would actually do. The amount is parsed forgivingly (a missing, malformed or non-positive amount, or a
currency that is not a three-letter ISO code, is a business outcome with a usage line, never an
exception); the currency defaults to EUR and is upper-cased. The handler then stores a short-lived per-chat
`ConversationState` carrying the parsed amount and currency as **structured values — never free-text the
Owner typed** — and asks for confirmation, writing nothing yet, exactly as `/note`'s no-text flow does.

**Explicit floor overrides learned salary weight (AC-05), asserted against F7.** The override is not a
special case in ranking: `PreferenceModelQuery.ExplicitStancesOf` projects a USD floor into a *negative*
`SalaryBand` stance on every band wholly below it (`SalaryBand.BandsWhollyBelow`), which flows through the
existing `PreferenceComponentCalculator` contradiction path so a learned *positive* weight on a below-floor
band is dropped and recorded as a conflict — explicit outranks inferred, by the same mechanism a preferred
country overrides a learned negative one. It is projected only for a USD floor, because the learner's
salary bands are USD-only and a non-USD floor cannot honestly name one — mirroring `SalaryBand.Of` refusing
to fabricate an FX rate. This is asserted directly against F7's query in `PreferenceModelQueryTests`.

Neither command runs an LLM or touches the CV (the CV crosses exactly one boundary, and it is not this
one), and every dynamic value reaches the reply through the one MarkdownV2 escaper.

**Deferred to T10.** As with T03–T07, this task ships the mechanism, not the live callback wiring.
`/floor` previews and stores its pending confirm state; the routing that resumes it on the Owner's
confirmation and applies the write through `Profile.SetSalaryFloor` and `IProfileRepository` is wired with
the dispatch rewire against the full command registry (T10). The reads and the preview here are live; only
the confirm-tap route is deferred.

## Links

[[../contracts/command-catalogue|catalogue]] §Profile and preferences · [[../../f7-preference-learning/index|F7]] · [[../../f4-cv-matching-ranking/contracts/match-schema|F4 CV rules]]
