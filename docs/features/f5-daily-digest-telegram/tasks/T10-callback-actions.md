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

## Implementation map

> Mechanical checklist. Copy the named exemplars; contested points resolved here per the docs.

**⚠️ Cross-feature prerequisite (resolved, action required).** The `Signal` domain type and the
`signals` table are owned by **F7-T01 (domain) and F7-T02 (migration)** — see
[[../../f7-preference-learning/tasks/T01-domain-preferences|F7-T01]] /
[[../../f7-preference-learning/tasks/T02-preference-persistence|F7-T02]] ("the `signals` table is created
here even though F5 and F6 write to it — F7 owns the schema, they own the rows"). Neither is built yet.
**Do not invent a Signal type in F5.** Options, cheapest first: (a) pull F7-T01 + F7-T02 forward as a
prerequisite of this task (they are M+S, and F7-T03 already assumes F5 writes signals); (b) if F7 is not
yet ready, land the callback/action/keyboard half of T10 now and gate signal capture behind an
`ISignalWriter` port (defined in Domain, implemented against the F7 table once it exists) so the
"same-transaction capture" property is wired but the row target arrives with F7. **Recommended: (a)** —
F7-T01/T02 are small and T10's AC-08 ("a Signal captured in the same transaction, carrying the job's
facts at that moment") cannot be truly satisfied without the table. Confirm with the tracker before
starting; if (b) is chosen, note it in the `Delivered` section as a known follow-up.

**Layer note.** T10 is a **telegram** task: the `CallbackHandler` lives in `JobHunter.Telegram`
(alongside `Transport/`), because it parses `callback_data` and calls `answerCallbackQuery` /
`editMessageReplyMarkup` — Telegram-client concerns. The action-apply + signal-capture *transaction* is
an Application concern it delegates to (a handler/command), keeping the arch arrow Telegram → Application.

**Payload (contract, fixed).** `callback_data = "{action}:{shortId}"`, ≤15 bytes.
`action ∈ open|ign|sav|app`. `shortId = base64url(HMAC-SHA256(cardKey, botSecret)[0..8])` (11 chars).
The HMAC key is the bot secret (invariant 12: from config, never logged). Resolve `shortId` through
`digest_cards` — an id that no longer resolves → a plain "this role has closed" message, never a silent
no-op (AC-09). A forged/unparseable id → a clear message (never a crash).

**Files to create**
- `src/JobHunter.Telegram/Callbacks/CallbackHandler.cs` — parse payload, resolve short id, dispatch.
- `src/JobHunter.Telegram/Callbacks/CallbackDataCodec.cs` — the HMAC short-id encode/resolve pair
  (pure, unit-tested against forged/expired/valid ids). Mirror `Auth/OwnerAuthorizer.cs` for structure.
- `src/JobHunter.Application/Actions/RecordCardActionCommand.cs` (+ handler) — applies the action and
  captures the signal **in one transaction** (AC-08); a signal never a separate step that can fail alone.
- `src/JobHunter.Domain/Abstractions/ICardActionStore.cs` — port for the action + facts-snapshot write.
- Wire `CallbackHandler` into `Transport/OwnerGatedUpdateProcessor.cs` (it already routes updates;
  callbacks are `update.CallbackQuery`, gated by the same allowlist as messages).

**Acknowledgement table (contract, fixed).** Ignore→`Won't show similar` + keyboard `[ Ignored ]`;
Save→`Saved` + `[ Open ] [ Saved ✓ ] [ Applied ]`; Applied→`Marked as applied` + `[ Open ] [ Applied ✓ ]`;
Open→no ack, keyboard unchanged. **`Won't show similar` is contract phrasing (D7)** — assert it verbatim.

**Facts snapshot (AC-08).** Capture the job's facts *at the moment of the tap*, not by joining to `jobs`
later (a later edit must not rewrite what the Owner reacted to — F7-T03 asserts this directly). The
command reads the job's current facts and writes them into the signal row in the same transaction.

**Tests** (`tests/JobHunter.Telegram.Tests/Callbacks/` + `tests/JobHunter.Application.Tests/Actions/`)
- All four actions: recorded, acknowledged, keyboard updated (AC-03), one per case.
- Ack latency ceiling asserted (QG-3) via `FakeClock` — no real wait.
- Signal captured in the same transaction with a complete facts snapshot (AC-08).
- Tap on closed/missing job → plain message, nothing invalid recorded (AC-09).
- Forged/unresolvable short id → clear message, never a silent no-op.
- Double-tap is idempotent and re-acknowledges (reuse the delivery-log idempotence pattern from T08).
- `CallbackDataCodec` forged-HMAC rejection, round-trip, wrong-secret rejection.

**Gotchas:** the bot secret is config, never logged/spanned (invariant 12); no CV anywhere near here.

## Links

[[../contracts/telegram-messages]] §Callback payloads · [[../../f7-preference-learning/index|F7]] ·
[[../../f7-preference-learning/tasks/T01-domain-preferences|F7-T01]] (Signal owner)
