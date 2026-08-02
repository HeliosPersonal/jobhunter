# T10 — Configuration, options validation, Infisical

**Layer:** infra/config · **Deps:** T03 · **Est:** M · **Owner:** Viacheslav

## What

`AddEnvVariablesAndConfigureSecrets()` following the `overflow` implementation: skipped
entirely in Development, Universal Auth against Infisical in Staging/Production, pulling
`/app/connections`, `/app/auth` and `/app/services`, mapping `SCREAMING__SNAKE` to `Colon:Separated`,
hard-failing in Production when nothing is returned. Plus the options convention: every adapter has
an options class with `SectionName`, bound and `Validate().ValidateOnStart()`.

## Done when

- Development starts with no Infisical credentials configured.
- Production with an unreachable Infisical exits non-zero and never reports ready (AC-09).
- A missing required option fails startup with a message naming the key (AC-09).
- Infisical values take precedence over ConfigMap values for the same key.
- No secret value appears in any log line — asserted by `SecretRedactionTests` (gate G6).

## Links

[[../../../00-overview/adr/0011-infisical-secrets|ADR-0011]] · [[../../../engineering/security]] §3
