# T04 — API host: auth, fallback-deny, OpenAPI

**Layer:** api · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

`JobHunter.Api` wiring: Keycloak JWT validation, the two scopes, the **fallback-deny**
policy so a new endpoint is protected by default, the subject check, problem-details error handling,
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
