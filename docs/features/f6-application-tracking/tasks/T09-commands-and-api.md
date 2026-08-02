# T09 — Telegram commands and API endpoints

**Layer:** telegram/api · **Deps:** T04, T06, T07 · **Est:** M · **Owner:** Viacheslav

## What

`/pipeline`, `/note` and status callbacks in the bot; the five owner-scoped endpoints from
[[../contracts/application-api|the contract]]. The refusal response names a remedy, because a refusal
without one is just an obstacle.

## Done when

- Every endpoint declares its scope explicitly; without it the request is refused (AC-09).
- A refused transition returns a body naming the rule and the remedy (AC-10).
- `/pipeline` renders in the same scannable card form as the digest.
- Status changes from the API and from Telegram record different `source` values.
- The endpoints appear in the OpenAPI document with examples.

## Links

[[../contracts/application-api]] · [[../../f9-search-and-api/index|F9]]
