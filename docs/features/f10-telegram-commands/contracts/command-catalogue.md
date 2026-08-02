---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "06-07"
ticket: ""
tags: [sdlc/stage-06, feature/f10-telegram-commands, mvp, jobhunter]
---

# Command catalogue

> **All twenty commands.** This document and `CommandRegistry` are asserted to be a bijection: a
> command here without a descriptor, or a descriptor without a heading here, fails the build
> ([[../sad|SAD]] §10 QG-1). Every heading below is a `ContractAnchor`.

**Legend.** *Scope* — `Owner` (any command) or `Operator` (system state). *State* — ✎ means it
changes state and therefore requires a confirmation tap ([[../sad|SAD]] §6.3).

---

## Digest and discovery

### `/digest`
**Scope** Owner · **State** read · **Args** none

Re-renders today's digest from stored state. **Does not re-deliver** — it writes no delivery-log rows
and cannot re-send the morning's cards ([[../../f5-daily-digest-telegram/adr/0002-delivery-idempotence|ADR-F5-0002]]).
If no digest exists yet, says so and gives the next scheduled time.

### `/more [count]`
**Scope** Owner · **State** read · **Args** `count` — optional, 1–20, default 5

The next cards below today's cut, in rank order, from the same stored digest. Re-ranking mid-morning
would make the ordering unstable, so this paginates rather than recomputes ([[../PRD]] §8). Reports
how many remain.

```
Next 5 of 23 below the cut.

*Backend Engineer, Payments*
Monzo · Series\-G · London / Remote UK
💰 90–115k GBP \(est, low conf\) · 🎯 *64*
• Kafka and event sourcing named as core
• Remote within UK only — outside your band
[ Open ] [ Ignore ] [ Save ] [ Applied ]
```

### `/search <query>`
**Scope** Owner · **State** read · **Args** free text with optional inline filters

Full-text search over the corpus via [[../../f9-search-and-api/index|F9]]. Inline filters are
`key:value` and may appear anywhere in the query:

| Filter | Example |
|---|---|
| `tech:` | `/search tech:kafka tech:azure distributed` |
| `stage:` | `/search stage:seriesb staff engineer` |
| `country:` | `/search country:de country:nl backend` |
| `min:` | `/search min:70 platform` (minimum score) |
| `since:` | `/search since:30d kafka` |
| `closed:` | `/search closed:yes kafka` (default excludes closed) |

Returns the top 10 as cards, the total found, and the leading facets so the next query can be
narrower. Empty result suggests dropping the most restrictive filter rather than returning nothing.

### `/hidden`
**Scope** Owner · **State** read · **Args** none

What today's ranking suppressed, grouped by reason, each with its evidence. This is
[[../../../CONTEXT]] invariant 11 made interactive — the digest footer gives the count, this gives the
jobs.

```
34 hidden today.

*Below salary floor* — 21
  Learned: 34 of your last 38 ignores were below 170k EUR
  [ Show these ] [ Turn this off ]

*Timezone incompatible* — 9
  AMER\-only roles, not remote
  [ Show these ]

*Employment type not sought* — 4
  Contract roles; your profile says full\-time only
  [ Show these ] [ Change profile ]
```

---

## Pipeline

### `/saved`
**Scope** Owner · **State** read · **Args** none

Saved roles, newest first, as cards with their current status. A saved role whose posting has closed
is marked.

### `/pipeline`
**Scope** Owner · **State** read · **Args** none

Everything grouped by status with counts, most recently active first within each group. Each entry
carries the buttons for its legal next transitions ([[../../f6-application-tracking/contracts/application-api|F6 transition matrix]]),
so advancing costs one tap and no second command.

```
*Pipeline* — 12 saved · 8 applied · 3 interview · 21 rejected

*Interview* \(3\)
  Snowflake · Staff Backend Engineer · applied 14 Jul · 5d in stage
  [ Offer ] [ Rejected ] [ Note ]
```

### `/due`
**Scope** Owner · **State** read · **Args** none

Applications past their stage threshold, with a suggested action each. The same set the 08:00
reminder sweep uses ([[../../f6-application-tracking/sad|F6 SAD]] §6.2) — this is the pull version of
that push.

### `/note [text]`
**Scope** Owner · **State** ✎ · **Args** free text, optional

Attaches a note to the most recently touched application. With no text, enters the multi-step flow
and asks. With no recent application, asks which one and offers the last five.

Notes are never logged — only their length ([[../../f6-application-tracking/data-model|F6 data model]]).

---

## Company

### `/company <name-or-domain>`
**Scope** Owner · **State** read · **Args** company name or canonical domain

The company's dossier: warnings first, then claims by category, each with its observed date and a
link to its source ([[../../../CONTEXT]] invariant 5). Also shows live roles and any application history.

Resolution is forgiving — `stripe`, `Stripe`, and `stripe.com` all match. An unknown company is said
plainly and offered as an addition to the registry, never returned as an empty result (AC-11).

### `/research <name-or-domain>`
**Scope** Owner · **State** ✎ · **Args** company name or domain

Requests a dossier, or a refresh if the existing one is stale. Confirms with the freshness of what
already exists so a needless refresh is visible before it is paid for. The result arrives with
tomorrow's digest ([[../../f8-company-research-agent/PRD|F8]] AC-05).

---

## Profile and preferences

### `/cv`
**Scope** Owner · **State** ✎ · **Args** none, or a document upload

Shows the active CV version, when it was activated, and how many current matches were computed
against it. **Never shows CV content** — the F4 boundary holds here
([[../../f4-cv-matching-ranking/contracts/match-schema|F4 CV handling rules]]).

Uploading a document in reply activates a new version, which re-stales existing matches and queues a
re-match of the last 30 days ([[../../f4-cv-matching-ranking/adr/0002-cv-versioning-and-restaling|ADR-F4-0002]]).
Confirmation names both consequences and the estimated cost before anything happens.

### `/prefs`
**Scope** Owner · **State** read · **Args** none

Every learned weight rendered as one sentence with its evidence, grouped by dimension — the chat face
of [[../../f7-preference-learning/adr/0002-evidence-threshold-and-explainability|ADR-F7-0002]].

```
*Learned preferences* — model v7, 412 signals, fitted 28 Jul

*Salary*
  Below 170k EUR — strongly negative
  34 of your last 38 actions on these were ignores
  [ Turn off ]

*Country*
  Germany — positive · Netherlands — positive
  11 of 14 saves were in these
  [ Turn off ]
```

With fewer than 200 signals it reports how many more are needed rather than showing an empty model.

### `/forget <dimension>`
**Scope** Owner · **State** ✎ · **Args** dimension name, or nothing to pick from a list

Disables a learned weight. Takes effect on the next ranking, not mid-Run, so a single Run's ordering
stays internally consistent. Not relearned until its supporting evidence doubles
([[../../f7-preference-learning/PRD|F7]] AC-06). The reply says exactly when it takes effect.

### `/floor <amount> [currency]`
**Scope** Owner · **State** ✎ · **Args** amount, optional ISO currency (default EUR)

Sets the explicit salary floor on the Profile. Explicit beats learned
([[../../f4-cv-matching-ranking/PRD|F4]] AC-05), so this overrides whatever F7 inferred. Confirmation
states how many of today's jobs it would have affected — the change is previewed before it is made.

---

## Operations

### `/status`
**Scope** Operator · **State** read · **Args** none

Last Run's outcome, cost against ceiling, counts, and any degraded sources. The first thing
[[../../../operations/runbooks|R1]] asks for.

```
*Last run* — 2 Aug, delivered 07:00

State: *Delivered* · 4h 51m
Cost: *$1\.04* of $2\.00 ceiling
127 discovered → 51 matched → 9 delivered · 34 hidden
⚠️ 1 source quarantined: greenhouse\/acme
```

### `/cost [month]`
**Scope** Operator · **State** read · **Args** optional `YYYY-MM`, default current

Spend for the calendar month plus the current Run, broken down by stage and tier, against the monthly
projection ([[../../../operations/infrastructure|infrastructure]] §8). Flags estimate-vs-actual drift
above 20%, which is how a stale pricing table surfaces.

### `/sources`
**Scope** Operator · **State** read · **Args** none

Per-provider health over the last 24 hours: attempts, successes, quarantined sources with their
release time. Quarantined entries carry a release button, which is [[../../../operations/runbooks|R4]]'s
main action without a terminal.

### `/run`
**Scope** Operator · **State** ✎ · **Args** none

Starts a Run immediately. Confirmation names the estimated cost and the jobs in scope. Refused with
an explanation if a Run is already live — there is at most one
([[../../f3-claude-batch-enrichment/data-model|F3 data model]]).

### `/redeliver`
**Scope** Operator · **State** ✎ · **Args** none

Re-delivers today's digest. Safe by construction: the delivery log means already-sent cards are not
sent again ([[../../f5-daily-digest-telegram/adr/0002-delivery-idempotence|ADR-F5-0002]]), and the
confirmation says how many cards would actually go out — usually zero, which is the point.

---

## Meta

### `/start`
**Scope** Owner · **State** read · **Args** none

Confirms the chat is authorised and shows the grouped command list. **An unauthorised chat receives
nothing** — the attempt is logged and the catalogue is never revealed (AC-10, [[../PRD]] §6.1).

### `/help [command]`
**Scope** Owner · **State** read · **Args** optional command name

Without an argument: all commands grouped by the sections above, one line each. With one: that
command's usage line, arguments and an example.

### `/cancel`
**Scope** Owner · **State** read · **Args** none

Abandons any pending multi-step command or confirmation (AC-08). Always available, and a no-op with
nothing pending — never an error.

---

## Client menu

`BotMenuSynchroniser` generates the menu from the registry at startup, so it cannot drift
([[../sad|SAD]] §4 S5). Operator-scoped commands are included — there is one Owner, and hiding them
would only make recovery harder to find.

## Argument parsing

| Rule | Behaviour |
|---|---|
| Missing required argument | Enter the multi-step flow and ask — never an error reply |
| Unknown inline filter | Treated as search text, with a note that it was not a filter |
| Malformed value (`min:abc`) | Named explicitly, with the usage line |
| Extra arguments | Ignored, with a note |
| Quoted phrase | Preserved as one term |

Arguments are parsed into typed values and never concatenated into a query or filter expression
([[../../f9-search-and-api/sad|F9 SAD]] §8).

## Unknown commands

Matched by Damerau–Levenshtein against registry names; distance ≤ 2 suggests, otherwise the grouped
list. Never an LLM ([[../adr/0002-no-conversational-fallback|ADR-F10-0002]]).

```
Unknown command `/pipline`\.

Did you mean */pipeline*? [ Yes, run it ]
Or /help for everything\.
```

## Related

[[../sad]] §5 · [[../test-plan]] · [[../../f5-daily-digest-telegram/contracts/telegram-messages|F5 message contract]]
