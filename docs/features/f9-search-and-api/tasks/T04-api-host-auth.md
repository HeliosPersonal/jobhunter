# T04 — API host: auth, fallback-deny, OpenAPI

**Layer:** api · **Deps:** — · **Est:** M · **Owner:** Viacheslav

> **Blocked by [[../../../ARCHITECTURE-OPEN-DECISIONS|O2]]** (is the API internet-facing behind
> Keycloak, or cluster-internal only). This is the one F9 task the readiness gate marks `[?]`; every
> other F9 task is ready. The decision changes the ingress and exposure posture but not the auth code
> below.

## What

`JobHunter.Api` wiring: Keycloak JWT validation, the two scopes (`jobhunter:read`, `jobhunter:admin`),
the **fallback-deny** policy so a new endpoint is protected by default, the subject (`sub` == Owner)
check applied on **both** the read and admin policies per ADR-0014, problem-details error handling,
and OpenAPI generation with Scalar.

## Done when

- The fallback policy is `RequireAuthenticatedUser`; an endpoint registered without a policy is still refused (AC-06).
- A valid token for a subject other than the Owner is refused with 403, not accepted.
- `/alive` and `/ready` are the only anonymous endpoints, and expose no business data.
- Errors are RFC 7807 with no internal detail in the body.
- The OpenAPI document is served and rendered by Scalar.
- Rate limiting is applied per token, in addition to Cloudflare's edge limiting.

## Links

[[../../../00-overview/adr/0014-keycloak-api-telegram-allowlist|ADR-0014]] · [[../../../engineering/security]] §2
