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

# 0014 — Keycloak OIDC for the API; chat-id allowlist for the bot

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

There are two inbound surfaces. The Telegram bot receives updates from Telegram's servers and must
serve exactly one human. The HTTP API exposes read models, search and admin operations, and is
intended to be internet-facing so a reviewer can click a live link
([[../idea-brief]] §15). The system is single-Owner ([[../../CONTEXT]] invariant 9), so we need
authentication without building an identity system.

## Decision drivers

- Zero user management: no sign-up, no password reset, no roles beyond Owner.
- Keycloak already runs on helios with a realm-per-project convention.
- Admin operations (trigger a Run, re-index, replay a stage) are destructive and must not be open.
- Telegram's update model has no notion of *our* identity — the chat id is the only signal.

## Considered options

1. **A static API key in a header.**
2. **Keycloak OIDC for the API; chat-id allowlist for the bot.**
3. **Full Keycloak-brokered social login with an account model.**
4. **No auth; cluster-internal only, no ingress.**

## Decision outcome

**Chosen: Option 2.**

- **API:** JWT bearer validation against the helios Keycloak realm `jobhunter`. Two scopes:
  `jobhunter:read` for read models and search, `jobhunter:admin` for mutations and operational
  endpoints. Every endpoint declares one explicitly; the default is deny. The `sub` claim must match
  the configured Owner subject — a valid token for a different subject is a 403, not a 200.
- **Bot:** an `OwnerAuthorizer` filter at the very front of the update pipeline drops any update
  whose `chat.id` is not in the configured allowlist, before routing, logging the rejected id at
  warning level. This mirrors `wisewizard`.
- **Health and metrics:** `/alive` and `/ready` are anonymous (kubelet needs them) and expose no
  business data. The Hangfire dashboard is cluster-internal only and additionally requires
  `jobhunter:admin`.

A static API key is rejected: it cannot be rotated without a redeploy and has no scope concept.
A full account model is rejected as building for a user who does not exist.

## Consequences

**Positive**
- Standard OIDC with no identity code to write; token lifetime and rotation are Keycloak's problem.
- Scope separation means a read token accidentally leaked cannot trigger a Run or re-index.
- The bot rejects unknown chats before any handler executes, so an unauthorised update cannot reach the domain.

**Negative**
- Keycloak becomes a hard dependency of the API (not of the pipeline — the Worker has no inbound surface).
- Obtaining a token for manual `curl` is a small friction; a documented client-credentials snippet
  lives in the runbook.

**Neutral**
- If the system ever became multi-user, the realm and scope model already exist; only an account
  aggregate would be added.

## Links

- SAD: [[../sad]] §3, §8
- Engineering: [[../../engineering/security]]
- Feature: [[../../features/f9-search-and-api/index|F9]]
