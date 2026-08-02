---
status: Accepted
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "M"
stage: "04-05"
ticket: ""
tags: [sdlc/stage-04, adr, jobhunter]
---

# 0010 — Kustomize + GHCR + GitHub Actions on a self-hosted runner

- **Status:** Accepted
- **Date:** 2026-08-02
- **Deciders:** Viacheslav

## Context

Three images must be built, pushed and deployed to two environments (`apps-staging`,
`apps-production`) on the helios k3s cluster. The cluster sits behind a home router with a dynamic
IP and is not reachable from the public internet, so a hosted CI runner cannot `kubectl apply`
against it. The sibling `overflow` project already solved exactly this.

## Decision drivers

- The k3s API server is not publicly exposed and must stay that way.
- Two environments differing only in namespace, replica counts, host names and config — an overlay
  problem, not a templating problem.
- One engineer: the deployment path must be debuggable at 23:00 without a GitOps control plane to reason about.
- Proven pattern in a sibling project on the same cluster.

## Considered options

1. **Helm charts + `helm upgrade` from a hosted runner through an exposed API server.**
2. **Kustomize base + overlays, applied by a self-hosted GitHub Actions runner on the cluster node.**
3. **GitOps: ArgoCD or Flux reconciling from the repository.**
4. **Aspire manifest generation → `aspirate` → k8s.**

## Decision outcome

**Chosen: Option 2**, mirroring `overflow`.

- `k8s/base/<service>/` holds `deployment.yaml`, `service.yaml`, `kustomization.yaml` for each of the
  three deployables. `k8s/overlays/{staging,production}/` add the namespace, ingress, replica counts,
  image tags and the environment ConfigMap.
- Images are built by `docker/build-push-action` with GitHub Actions cache and pushed to
  `ghcr.io/<owner>/jobhunter-{api,worker,telegram}:<sha>`, plus `:latest` only from `main`.
- The deploy job runs on the **self-hosted runner on the helios node**, substitutes the commit SHA
  and the Infisical bootstrap credentials into the overlay via `sed`, then `kubectl apply -k` and
  `kubectl rollout status` per deployment.
- Branch mapping: `develop` → staging, `main` → production. Production additionally runs a
  post-deploy smoke test against `/health` for each service.

GitOps is rejected for now: it adds a controller to operate and an indirection to debug, for a
benefit (drift reconciliation) that matters at team scale, not at one engineer with one cluster.
Aspire manifest generation is rejected because Aspire is a local-development tool here
([[0013-aspire-local-dev-only|ADR-0013]]) and generated manifests are harder to review than the
twelve short YAML files we would otherwise hand-write.

## Consequences

**Positive**
- The cluster API stays private; nothing is exposed for CI.
- Overlays are plain YAML — reviewable in a diff, no template language to debug.
- Identical to `overflow`, so the runbook, the scripts and the muscle memory are shared.

**Negative**
- The self-hosted runner is a single point of failure for deployment and must be patched and trusted.
- `sed`-based substitution is crude; a typo in a placeholder fails at apply time rather than at
  review time. Mitigated by a `kubectl kustomize` preview step before every apply.

**Neutral**
- Migrating to GitOps later is additive: point Argo at `k8s/overlays/production` and stop applying from CI.

## Links

- SAD: [[../sad]] §7
- Engineering: [[../../engineering/ci-cd]], [[../../engineering/deployment]]
- Related: [[0011-infisical-secrets]], [[0013-aspire-local-dev-only]]
