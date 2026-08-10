# T06 — MarkdownV2 escaping and formatters

**Layer:** telegram · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

`MarkdownV2Escaper`, `DigestHeaderFormatter` and `CardFormatter` per
[[../contracts/telegram-messages|the contract]]. The escaper is the single path to a message, enforced
by an architecture test that forbids interpolating a non-constant directly into message text.

## Done when

- Every character requiring escape in MarkdownV2 is escaped; the hostile-input set renders safely.
- Truncation counts graphemes, not bytes — a flag emoji at the boundary is not split.
- The header is six lines or fewer in every variant (AC-01).
- An architecture test forbids raw interpolation into message text.
- Every layout in the contract has a committed snapshot, reviewed in diffs.

## Delivered

The formatters and the one escaper they all route through share a single `JobHunter.Telegram.Formatting`
namespace, so one architecture test can fence the whole message surface. That namespace now lives in the
`JobHunter.Telegram.Transport` project (the shared send-path adapter composed by both the Worker and the
bot host, see [[T08-delivery-idempotence]]); the namespace was kept identical across the project move, so
every reference below and the `SourceScan` still resolve unchanged.

- **`MarkdownV2Escaper`** (Telegram/Formatting) — the **canonical** escaper, now shared. `Escape` backslash-
  escapes every one of the eighteen MarkdownV2 specials `_ * [ ] ( ) ~ ` > # + - = | { } . !` — one
  unescaped special silently fails the *whole* send, so the set is exhaustive by construction. `Truncate`
  clips on **graphemes** (`StringInfo.GetTextElementEnumerator`), backing off to the last word boundary and
  appending an ellipsis, so a flag emoji, a combining mark or a CJK glyph at the 60-char boundary is never
  split into a broken half. `FormatThousands` renders whole-thousands money as `185k` and sub-thousand
  verbatim. The F9 `/search` renderer's minimal placeholder escaper (which anticipated this consolidation)
  was **deleted** and `SearchResultRenderer` / `SearchCommandHandler` now use this one — a single
  implementation, so the escape set cannot drift between the digest and `/search`.
- **`CardView` / `CardSalary`** (Telegram/Formatting) — the display projection of a `DigestCard`. The domain
  card snapshots only the score, reasons and idempotence key; the human strings (title, company, salary) are
  joined from the `jobs` read model at delivery (T08), and this view is where they meet the layout. It
  carries **nothing about the Owner** — the CV crosses exactly one boundary, and it is not this one.
- **`HeaderView` / `HeaderOpportunity` / `DigestMode`** and **`FooterView` / `FooterTally`** — the header
  and footer projections and the four-way variant enum (`Full`, `NothingNew`, `Partial`, `BudgetReached`).
- **`CardFormatter`** — bold title truncated to 60 graphemes at a word boundary, `company · stage · location`
  (an `Unknown` stage is omitted, never printed), an optional `💰 … · 🎯 *score*` line where an **estimate is
  marked `(est, conf)` and never presented as fact**, a whole-number score, then **exactly three** reasons,
  each whitespace-collapsed (a `\n\n` becomes a single space) and capped at 90 graphemes.
- **`DigestHeaderFormatter`** — one fixed small set of lines per variant, so the header is **≤ 6 content
  lines in every variant** (AC-01), asserted for all four. Every degraded day still renders a header
  (ADR-F5-0001): `(partial)` / `(reduced)` labels, the self-explaining "nothing new" reassurance, the best
  opportunity promoted above the fold, and the hidden count in the header where D7 is made visible.
- **`DigestFooterFormatter`** — renders **only when it has something to say** (returns null otherwise), with
  the still-processing and degraded-source lines omitted when zero; the hidden breakdown is invariant 11 made
  visible.
- **Rule 9 architecture test** (`ConventionRulesTests.Rule9`, with a `Violations` fixture proving it goes
  red) — a `SourceScan` over `Telegram/Formatting` forbids interpolating a raw value straight into active
  MarkdownV2 markup (e.g. `$"*{title}*"`): the value must pass through `Escape` and the markup be an adjacent
  constant. The only path to message text is through the formatter.

Also folds in a pre-existing architecture-gate fix unrelated to the layout: `Infrastructure`'s `DeliveryLog`
adapter (from T02) was `public` with a non-sanctioned name, tripping rule 8; it is reached only through the
`IDeliveryLog` port, so it is now `internal` like every other adapter.

**9 corpus snapshots** (`Telegram.Tests/Fixtures/rendering-corpus`, one per contract layout: four headers,
four cards incl. a hostile-input card, one footer — the exact bytes the bot would send, diff-reviewed), 21
escaper tests, 18 card tests, 11 header tests (incl. the AC-01 six-line assertion over all variants), 8
footer tests and 2 architecture tests (production green + violation red); the whole solution builds with zero
warnings.

## Links

[[../contracts/telegram-messages|contract]] · [[../test-plan]] §The rendering corpus
