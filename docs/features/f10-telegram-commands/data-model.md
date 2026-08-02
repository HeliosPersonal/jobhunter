---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "08"
ticket: ""
tags: [sdlc/stage-08, feature/f10-telegram-commands, mvp, jobhunter]
---

# Data model — f10-telegram-commands

> **Owns:** `command_invocations`. Conversation state lives in **Redis**, not PostgreSQL — see below.
> **References (read-only):** everything. F10 is a surface, not a store.

## Why almost nothing is owned here

Every command answers from a query service another feature owns
([[sad|SAD]] §4 S2). Introducing tables here would create a second copy of data that must be kept
correct, and would let the chat and the API disagree about what a saved job is. The only genuinely
new facts F10 produces are *that a command was run* and *that a chat is mid-conversation*.

## `command_invocations`

An append-only audit and usage log. Its purpose is the metric in [[PRD]] §7 — which parts of the
system the Owner actually reaches for — and diagnosing a command that misbehaves.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | uuid | PK | |
| `chat_id` | bigint | NOT NULL | |
| `command` | text | NOT NULL | registry name, no slash |
| `outcome` | text | NOT NULL | `Succeeded`, `Unknown`, `Unauthorised`, `Malformed`, `Throttled`, `Cancelled`, `Failed` |
| `duration_ms` | integer | NOT NULL | feeds the p95 NFR |
| `arg_count` | smallint | NOT NULL | **count only** |
| `invoked_at` | timestamptz | NOT NULL | |

**Argument *content* is never stored**, only how many there were. A `/search` query can contain
anything the Owner typed, and `/note` certainly does. The count is enough for usage analysis; the
text is not ours to keep ([[sad|SAD]] §8, and the same rule the notes table follows).

**Access patterns:** "invocations by command over 7 days" → `idx_command_invocations_command`;
"unknown-command rate" → filter on `outcome`.
**Retention:** 180 days, pruned with the other operational logs.

## Conversation state — Redis, not PostgreSQL

Key `{env}:jobhunter:convstate:{chat_id}`, value a small JSON document, **TTL 300 seconds**.

```json
{
  "command": "note",
  "awaiting": "text",
  "context": { "applicationId": "0192f8a1-..." },
  "startedAt": "2026-08-02T21:14:09Z"
}
```

Redis rather than a table for one reason that matters: **the TTL is the expiry mechanism.** A
Postgres table would need a sweeper job, and a sweeper that fails leaves a chat permanently wedged —
every subsequent message swallowed as input to a command the Owner forgot about. With a native TTL
there is nothing to fail. A pod restart cannot wedge a chat either.

The trade is that pending state is lost if Redis is flushed. That is the correct direction: losing a
half-typed note is a shrug, and the Owner simply re-issues the command.

Confirmation nonces use the same store under `{env}:jobhunter:confirm:{nonce}` with a 120-second TTL
and are deleted on use, which is what makes them single-use ([[sad|SAD]] §6.3).

## Indexes

| Index | Columns | Serves |
|---|---|---|
| `idx_command_invocations_command` | `command_invocations(command, invoked_at DESC)` | per-command usage |
| `idx_command_invocations_outcome` | `command_invocations(outcome, invoked_at DESC)` | unknown / throttled rates |
| `idx_command_invocations_time` | `command_invocations(invoked_at)` | retention pruning |

## Handoffs

- **Reads** query services from F5, F6, F7, F8 and F9 — the same ones the API calls.
- **Writes** through existing services only: `/note` via F6's note service, `/forget` via F7's
  override service. F10 owns no write path of its own. `/cv` is **read-only metadata** — it reports
  the current CV status and version through F9's read endpoint and never uploads; CV upload is F4's
  boundary and is not reachable from any command ([[sad|SAD]] §2).
- **Produces** no events. A command that changes state does so through the owning feature, which
  publishes its own event as usual.

## Related

[[../../architecture/data-model]] · [[sad]] §7 · [[contracts/command-catalogue]]
