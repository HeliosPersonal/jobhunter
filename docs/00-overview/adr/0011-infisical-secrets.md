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

# 0011 — Infisical for runtime secrets; Terraform ConfigMap for non-secret config

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

The application needs an Anthropic API key, a Telegram bot token, PostgreSQL/RabbitMQ/Redis/
Typesense credentials and Keycloak client secrets. It also needs non-secret configuration that is
only knowable after the shared infrastructure is provisioned — hostnames, ports, vhost names, OTLP
endpoints. These are two different problems and conflating them is how secrets end up in git.

## Decision drivers

- Nothing secret in the repository, in image layers, or in logs ([[../../CONTEXT]] invariant 12).
- Infra-derived config (hostnames, connection-string skeletons) is naturally produced by Terraform
  reading the helios remote state — it should not be hand-copied into a Secret.
- `overflow` and `wisewizard` both already use Infisical with machine-identity auth; the tooling,
  the project and the runbook exist.
- Rotation must not require a repository commit.

## Considered options

1. **Plain Kubernetes Secrets, values injected by CI.**
2. **Sealed Secrets or the External Secrets Operator.**
3. **HashiCorp Vault.**
4. **Infisical SDK pulling at process startup, with a minimal k8s Secret carrying only the machine identity.**

## Decision outcome

**Chosen: Option 4**, matching the sibling projects.

- A committed `k8s/base/infisical/secret.yaml` holds three **placeholder** keys
  (`INFISICAL_CLIENT_ID`, `INFISICAL_CLIENT_SECRET`, `INFISICAL_PROJECT_ID`). CI substitutes the real
  values at deploy time from GitHub Secrets. This is the only secret material in the cluster.
- At startup, `AddEnvVariablesAndConfigureSecrets()` authenticates to Infisical with Universal Auth,
  pulls the environment-scoped secrets from `/app/connections`, `/app/auth` and `/app/services`,
  maps `SCREAMING__SNAKE` keys to `Colon:Separated` configuration keys, and injects them.
  **Skipped entirely in Development** (Aspire supplies connection strings locally); **hard-fails
  startup in Production** if the pull returns nothing.
- Non-secret, infra-derived config lands in the Terraform-managed `jobhunter-infra-config` ConfigMap,
  consumed via `envFrom`. Infisical wins on key collisions, so a secret can always override.

## Consequences

**Positive**
- One secret in the cluster instead of fifteen; rotation happens in Infisical without a deploy.
- Development needs no secret store at all.
- Production fails loudly rather than starting with empty credentials.

**Negative**
- Infisical is a startup-time dependency: if it is unreachable, production pods will not start.
  Accepted — a pod running without credentials is worse.
- A network call on the startup path adds a second or two to cold start.

**Neutral**
- The same mechanism serves all three deployables through one shared extension method.

## Links

- SAD: [[../sad]] §8
- Engineering: [[../../engineering/ci-cd]], [[../../engineering/security]]
- Related: [[0010-kustomize-ghcr-selfhosted-runner]]
