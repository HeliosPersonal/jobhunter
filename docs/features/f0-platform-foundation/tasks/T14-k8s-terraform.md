# T14 — Dockerfiles, Kustomize base and overlays, Terraform

**Layer:** deploy · **Deps:** T10 · **Est:** L · **Owner:** Viacheslav

## What

Three multi-stage Dockerfiles with layer-cached restore and `USER $APP_UID`. Kustomize
`base/` for api, worker, telegram, migrator and the Infisical placeholder secret; `overlays/staging`
and `overlays/production`. Terraform consuming the helios remote state to create the two databases
and the `jobhunter-infra-config` ConfigMap. All as specified in
[[../../../engineering/deployment|deployment]].

## Done when

- `kubectl apply -k k8s/overlays/staging` produces three running, ready deployments.
- The migrator Job completes before any Deployment becomes ready (AC-11).
- `jobhunter-worker` is `replicas: 1` with `strategy: Recreate` — asserted by a manifest test.
- No password appears in the Terraform-managed ConfigMap.
- `terraform apply` is idempotent; a second run reports no changes.
- Images run as non-root; a `docker run --rm <image> id -u` returns non-zero uid.

## Links

[[../../../engineering/deployment]] · [[../../../operations/infrastructure]]
