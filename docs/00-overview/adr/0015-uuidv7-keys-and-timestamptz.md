---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "S"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0015 — UUID v7 keys, `timestamptz` UTC, `numeric` money

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Small persistence conventions, decided once, that would otherwise be re-litigated in every one of
the ten features and would be expensive to change afterwards. All three concern data that crosses a
boundary: identifiers appear in URLs and Telegram callback payloads, timestamps drive a 07:00
schedule across DST, and salary figures are compared and averaged in the digest.

## Decision drivers

- Ids appear in `callback_data` (Telegram caps it at 64 bytes) and in public API URLs.
- Sequential integer keys leak volume and are guessable; random UUID v4 keys fragment B-tree indexes
  on insert-heavy tables (`raw_postings` grows by thousands a day).
- The digest reports average salary. Binary floating point is wrong for money, always.
- The schedule is `Europe/Kyiv`, which observes DST; storage must be unambiguous.

## Considered options

1. **`bigint` identity keys, `timestamp without time zone`, `double precision` money.**
2. **UUID v4 keys, `timestamptz`, `numeric` money.**
3. **UUID v7 keys, `timestamptz` UTC, `numeric(12,2)` money with an explicit currency column.**

## Decision outcome

**Chosen: Option 3.**

- **Keys:** UUID v7 (`Guid.CreateVersion7()`, native in .NET 9+), stored as `uuid`. Time-ordered, so
  index locality is preserved on insert-heavy tables; non-guessable, so exposing them is safe;
  globally unique, so a Job can be referenced from Typesense and Telegram without a lookup.
- **Timestamps:** `timestamptz`, always stored UTC, always read through `IClock`. No
  `DateTime.Now` anywhere — an architecture test forbids it. Cron schedules are declared in
  `Europe/Kyiv` so 07:00 stays 07:00 across DST ([[0004-hangfire-scheduling|ADR-0004]]).
- **Money:** `numeric(12,2)` plus a separate `char(3)` ISO-4217 currency column. `decimal` in C#.
  Salary *ranges* are two columns (`min`, `max`) plus a `period` enum (`Year`, `Month`, `Day`,
  `Hour`); an unparseable salary is stored as `NULL` with the raw string retained, never coerced.

Callback payloads use a short base64url encoding of the UUID plus an action code to stay inside
Telegram's 64-byte limit.

## Consequences

**Positive**
- Ids are safe to expose, cheap to index, and stable across stores.
- No DST bugs on the one schedule the product is named after.
- Salary arithmetic is exact; currencies are never silently mixed.

**Negative**
- 16-byte keys instead of 8. Irrelevant at this data volume.
- Every salary comparison must consider currency and period. That is correct, not incidental
  complexity — it is handled once in a `SalaryRange` value object.

**Neutral**
- `IClock` injection is required from the first task, which also makes time-dependent logic testable
  without waiting.

## Links

- SAD: [[../sad]] §8
- Data model: [[../../architecture/data-model]]
- Related: [[0003-postgresql-efcore-dapper]], [[0004-hangfire-scheduling]]
