# T05 — IJobSource port and Greenhouse adapter

**Layer:** scrapers · **Deps:** T04 · **Est:** M · **Owner:** Viacheslav

## What

The `IJobSource` port (streaming `IAsyncEnumerable<FetchedPosting>`) and the first
adapter. Greenhouse first because it is the most common and has the most awkward payload — its
`content` field is HTML-escaped HTML, which is a good forcing function for the decoding conventions
the other four will reuse. Includes content hashing with volatile-field stripping.

## Done when

- `IJobSource` streams; a 400-posting board is processed with bounded memory.
- Greenhouse `content` is double-decoded then HTML-stripped to plain text.
- `updated_at` and `requisition_id` are stripped before hashing, so a cosmetic touch is not a change.
- All eight standard fixtures pass ([[../test-plan|test-plan]] §Fixture corpus).
- A malformed posting inside an otherwise valid board is skipped and counted, not fatal.
- Zero network in every test.

## Links

[[../contracts/ats-endpoints|ATS endpoints]] §Greenhouse · [[../sad]] §5
