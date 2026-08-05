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

## Implementation map

> Mechanical checklist. This is a **tests** task — it adds coverage and a manual checklist, not
> production code. Extend the existing snapshot harness; do not build a parallel one.

**What already exists (build on it).** The snapshot harness is
`tests/JobHunter.Telegram.Tests/Formatting/RenderingCorpusSnapshotTests.cs`, asserting against
`tests/JobHunter.Telegram.Tests/Fixtures/rendering-corpus/*.snapshot.txt` with **CRLF normalised to LF**.
The four header shapes, card, footer and MarkdownV2 escaping are all covered from T06/T09. T12 *completes*
the corpus to ~200 cases and adds the splitting-boundary and hostile-input suites + the manual checklist.

**Files to edit / create**
- Extend `RenderingCorpusSnapshotTests.cs` (or add sibling `*SnapshotTests.cs` under `Formatting/`) with
  the missing layouts and the two suites below. Keep every fixture as a committed `.snapshot.txt` so a
  layout change shows in the PR diff (the whole point of the corpus).
- `tests/JobHunter.Telegram.Tests/Formatting/MessageSplittingTests.cs` — the 4096-boundary suite.
- `tests/JobHunter.Telegram.Tests/Formatting/HostileInputTests.cs` — one case per hostile-input table row.
- `docs/features/f5-daily-digest-telegram/contracts/live-smoke-checklist.md` (or under `operations/`) —
  the manual pre-release checklist; check the doc tree for an existing runbook to extend first.

**Splitting boundary (fixed criterion).** Telegram caps a message at 4096 chars. Assert splitting **just
under, at, and just over 4096**, and that a split **never occurs mid-card** — a card is atomic; the
splitter breaks between cards, never inside one. Find the splitter in `Transport/` (the notifier/pacer
already chunk sends) and drive it with a synthetic oversized digest.

**Hostile inputs (fixed criterion).** One case per row of [[../test-plan]] §rendering-corpus hostile
table: MarkdownV2 metacharacters in titles/companies, RTL/zero-width chars, over-long fields, emoji,
control chars. Each must render **safely with the layout intact** (escaping via
`Formatting/MarkdownV2Escaper.cs`, already built) — assert no unescaped metacharacter reaches output.

**Performance criterion.** The whole corpus must run in **under 10 s** so it is never skipped — keep it
zero-network, zero-database (pure formatter calls), and avoid per-case fixture file I/O in a hot loop.

**Manual live-smoke (fixed criterion, one-time).** The checklist covers one real message to a test chat
and verifies the **four action buttons work in a real client** before M4 is called done — some things
only break in the real Telegram client. This is executed once and its execution noted; it is not a CI
gate. Requires the bot token (from config/Infisical, never committed) and a test chat id.

**Depends on T09 and T11** — the degraded variants (T09) and the command outputs (T11) must exist before
their snapshots can be committed. Sequence T12 last in F5's rendering line.

**Gotchas:** normalise CRLF→LF when reading fixtures (Windows checkout); no CV or secret in any snapshot.

## Links

[[../test-plan]] §The rendering corpus
