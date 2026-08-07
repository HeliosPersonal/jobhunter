# T02 — Argument parser

**Layer:** app · **Deps:** T01 · **Est:** M · **Owner:** Viacheslav

## What

Forgiving positional parsing plus `key:value` inline filters, per
[[../contracts/command-catalogue|the catalogue]] §Argument parsing. Arguments become typed values;
they are never concatenated into a query or filter expression.

## Done when

- `/company stripe`, `/company Stripe` and `/company stripe.com` all resolve to the same company.
- A missing required argument enters the multi-step flow rather than returning an error (catalogue §Argument parsing).
- A malformed value names what was wrong and shows the usage line.
- An unknown inline filter is treated as search text, with a note.
- Quoted phrases survive as single terms; duplicate filters are deduplicated.
- A test asserts no parsed value reaches a query as raw concatenated text.

## Implementation

A pure Application coordinator, `ArgumentParser.Parse(arguments, descriptor, vocabulary)`, returning a
`ParsedArguments` record. It is static — it holds no state, mirroring the existing
`SearchCommandParser` — and it is total: it never throws on user input, so no line of chat can turn
into an error reply.

- **Typed values, never a blob (done-when #6).** Tokenisation lifts every recognised `key:value` filter
  out into a typed `ParsedFilter(Key, Value)` list, and the free text that remains carries no filter
  syntax. `ParsedArguments` therefore hands the dispatcher `FreeText` plus `Filters` as separate typed
  values — a query builder never receives `"min:70 platform tech:go"` as one concatenated string
  ([[../../f9-search-and-api/sad|F9 SAD]] §8). This is asserted directly.
- **Forgiving, per the catalogue table.** A missing *required* argument returns `NeedsInput` naming the
  argument — the entry point to the multi-step flow, never an error (done-when #2). An unknown inline
  filter is kept as free text with a `Note` (done-when #4). A value that cannot fit its declared
  `InlineFilterKind` (`min:abc`) returns `Malformed` with the offending token named and the generated
  usage line (done-when #3). Extra input to a no-argument command is ignored with a note.
- **The filter vocabulary is a parameter, not baked in.** `InlineFilterVocabulary` is the set of
  `InlineFilterSpec(key, kind)` a command understands; the dispatcher (T03) passes each command's own
  vocabulary. `InlineFilterVocabulary.None` means every `key:value` token is plain text, so a colon in
  an ordinary term is never an error. `InlineFilterKind { Text, Number, Duration, Boolean }` is what
  turns `min:abc` into a named, forgiving error rather than a silent mis-filter.
- **Positional faithfulness and phrases (done-when #1, #5).** A single positional token (`stripe`,
  `Stripe`, `stripe.com`) is handed to the query verbatim; the `stripe`/`Stripe`/`stripe.com`
  equivalence is `CanonicalDomain`'s job downstream, not the parser's. A double-quoted phrase survives
  as one term and is never read as a filter even if it holds a colon; recognised filters are
  deduplicated case-insensitively, and filter keys are matched case-insensitively and normalised to
  lower case.

## Links

[[../contracts/command-catalogue|catalogue]] §Argument parsing
