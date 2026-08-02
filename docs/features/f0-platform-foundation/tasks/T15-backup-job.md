# T15 — Nightly pg_dump backup to Azure Blob

**Layer:** ops · **Deps:** T14 · **Est:** M · **Owner:** Viacheslav

## What

A Kubernetes CronJob that runs a nightly `pg_dump` of `{env}_jobhunter`, compresses it, and uploads
it to an Azure Blob container with a retention window. Credentials come from Infisical, never from the
manifest. This is the artifact runbook R9 (disaster recovery) restores from — without it, R9 has
nothing to restore.

## Done when

- A CronJob in Kustomize `base/` dumps the database and uploads a timestamped, compressed object to
  Azure Blob on schedule.
- The Azure connection string and any credentials are resolved from Infisical at runtime; no secret
  appears in the manifest or image layers (invariant 12).
- Old objects past the retention window are pruned so the container does not grow unbounded.
- A restore rehearsal documented in runbook R9 recreates the schema and data from the latest object on
  a clean database.
- A failed backup raises an alert rather than failing silently.

## Out of scope

- Point-in-time recovery / WAL archiving (accepted debt; documented).
- Cross-region replication.

## Links

[[../../../engineering/deployment]] · [[../../../operations/runbooks]] R9 · [[../../../operations/infrastructure]]
