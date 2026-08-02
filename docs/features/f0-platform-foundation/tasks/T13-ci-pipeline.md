# T13 — CI pipeline: build, test, images, deploy to staging

**Layer:** ci · **Deps:** T12 · **Est:** L · **Owner:** Viacheslav

## What

`.github/workflows/ci-cd.yml` implementing [[../../../engineering/ci-cd|the CI/CD design]]:
`build-and-test` on a hosted runner with the coverage gate, `build-images` as a matrix over the
three services pushing SHA-tagged images to GHCR with GitHub Actions cache, `terraform` and
`deploy-staging` on the self-hosted `helios` runner.

## Done when

- A push to `develop` produces three SHA-tagged images and a successful staging rollout with no human action (AC-07).
- The coverage gate fails the `dotnet test` step itself, not a separate reporting step.
- `kubectl kustomize` renders both overlays as a PR check.
- The manifest preview runs before every apply so an unsubstituted placeholder is visible in the log.
- The PR pipeline completes in under 8 minutes.

## Out of scope

- Production deployment — gated on F5.
- Smoke tests — added with the production job.

## Links

[[../../../engineering/ci-cd]] · [[../../../00-overview/adr/0010-kustomize-ghcr-selfhosted-runner|ADR-0010]]
