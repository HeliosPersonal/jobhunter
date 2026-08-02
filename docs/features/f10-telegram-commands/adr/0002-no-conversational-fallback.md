---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, feature/f10-telegram-commands, jobhunter]
---

# F10-0002 — No LLM in the command path

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The bot has a language model behind it and a chat interface in front of it. The obvious next step —
and the one every user will eventually try — is to type a sentence instead of a command and expect an
answer. *"What did I apply to last week?"* is a reasonable thing to type into a chat.

Adding that is a small change: route unrecognised input to Claude with the read models as tools. The
question is whether it should be a small change we make.

## Decision drivers

- Every message is a real API call with real latency and real cost. The command path today is a
  database query answering in under two seconds.
- A chat interface sets an expectation of *general* competence. The moment it answers one sentence
  well, the next sentence will be harder, and the failure will read as the product being broken
  rather than out of scope.
- The commands are deterministic and testable. Natural-language routing is neither, and it would put
  an unassertable component on the path to state-changing operations.
- [[../PRD]] §3 already says this is not a conversational assistant. This ADR records why that is a
  decision rather than an omission.

## Considered options

1. **Fixed catalogue only.** Unknown input gets a suggestion or the command list.
2. **LLM intent routing** — map a sentence to a command and its arguments, then run it.
3. **Full conversational agent** with the read models as tools.
4. **LLM fallback only for unmatched input**, commands unchanged.

## Decision outcome

**Chosen: Option 1.**

Unrecognised input is matched by Damerau–Levenshtein against registry names: distance ≤ 2 suggests
the nearest command with a one-tap run button, otherwise the grouped list. Deterministic, instant,
free, and testable against a fixture set of misspellings.

Option 4 is the tempting middle, and it is the one worth arguing against explicitly. It looks
bounded — only unmatched input, only routing — but it is the same expectation-setting problem in a
smaller box. Once *"what did I apply to last week"* works, *"which of these should I chase"* is the
next thing typed, and that is a judgement call the system has no way to ground. The failure is not an
error message; it is a confident wrong answer in the product's most trusted channel.

There is also a concrete safety edge: intent routing on the path to `/run` or `/research` means a
model decides whether to spend money. The confirmation step would catch it, but the right answer is
not to have a probabilistic component there at all.

## Consequences

**Positive**
- Every command answers in under two seconds, at zero marginal cost, with a deterministic result.
- The command path has nothing unassertable in it, which is what makes the conformance suite meaningful.
- No expectation of general competence, so no disappointment when a sentence is not understood.
- State-changing operations are never reached through a probabilistic step.

**Negative**
- The Owner must learn 22 commands. Mitigated by the generated client menu, grouped `/help`, and
  typo suggestions — and by the fact that there is exactly one user, who wrote the catalogue.
- Genuinely useful natural-language queries are not possible. Accepted: `/search` with inline filters
  covers the retrospective questions, which is what the corpus is actually for.

**Neutral**
- Revisitable. If the command set proves too large to remember in practice — the unknown-command rate
  in [[../PRD]] §7 is the signal — intent routing over the *existing* catalogue is the smallest next
  step, and the registry already provides the target set. That would be a new ADR, not a quiet change.

## Links

- [[../PRD]] §3, §7, AC-09 · [[../sad]] §4 S6 · [[../contracts/command-catalogue]] §Unknown commands
