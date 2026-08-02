---
status: Living
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "L"
stage: "index"
ticket: ""
tags: [sdlc/stage-index, feature/f5-daily-digest-telegram, mvp, jobhunter]
---

# F5 · Daily Digest & Telegram Delivery

> **Feature index (MOC).** Every artifact for this feature, in reading order.

The product. Everything before F5 is machinery; this is the part the Owner actually experiences: one
message at 07:00 Europe/Kyiv that answers "is today worth my attention" in three seconds, followed by
scannable cards with four one-tap actions.

**F5 is milestone M4 — the first shippable release.**

## Reading order

1. [[PRD|PRD]] — what the 07:00 message must do, and what it must never do
2. [[sad|SAD]] — digest assembly, rendering, delivery idempotence, callback handling
3. [[data-model|Data model]] — `digests`, `digest_cards`, `delivery_log`
4. [[contracts/telegram-messages|Telegram message contract]] — layout, callback payloads, escaping
5. [[test-plan|Test plan]] — the rendering corpus and the duplicate-delivery suite
6. [[tasks/_epic|Epic]] → [[tasks/tracker|Tracker]] — 12 tasks

## Architecture decisions

- [[adr/0001-never-delay-the-digest|ADR-F5-0001]] — 07:00 is a hard commitment; ship partial rather than late
- [[adr/0002-delivery-idempotence|ADR-F5-0002]] — the delivery log is the idempotence mechanism

## Milestone

**M4 — The product.** Exit: a real digest lands at 07:00 with working Open/Ignore/Save/Applied buttons.

## Related

[[../f4-cv-matching-ranking/index|← F4]] · [[../f6-application-tracking/index|F6 →]] ·
[[../f7-preference-learning/index|F7]] (consumes the Signals F5 captures) · [[../../CONTEXT]] invariant 8
