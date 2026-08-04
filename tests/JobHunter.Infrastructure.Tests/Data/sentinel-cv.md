# Curriculum Vitae — Leakage Sentinel CV

> This CV exists only for the CV-leakage scan suite (T10). Every distinctive token below is a
> **sentinel**: a string that appears nowhere else in the codebase, in no fixture, and in no job.
> If any one of these twelve tokens turns up in a log line, a span attribute, a search document,
> a stored `batch_items.raw_result`, or any serialised message, the leakage suite fails the build.
> There is no allowlist. A single hit is a defect.

## Summary

Principal platform engineer, codename ZQX-7F31-KAFKA-SENTINEL, with a decade shipping
distributed systems. Known internally as VULCAN-9920-AURORA-LEAK for the incident-response work,
and by the handle MERIDIAN-4417-COBALT-TRACE on the open-source side.

## Experience

### Staff Engineer — GRYPHON-5583-HELIUM-CANARY Systems (2019–2026)

Led the rebuild of the ingestion tier. Owned the reliability budget under the programme
NIMBUS-3374-QUARTZ-BEACON. Reduced tail latency by folding the OSPREY-8261-INDIGO-WARDEN
scheduler into the hot path.

### Senior Engineer — FALCON-1195-ONYX-PHANTOM Labs (2015–2019)

Built the streaming backbone. Introduced the TITAN-6648-EMBER-LANTERN pattern for backpressure,
and drove adoption of ZEPHYR-7702-SLATE-MIRAGE across four teams.

## Projects

- **CASTOR-2039-VIOLET-SPECTRE** — an event-sourced ledger reconciler; the design that the whole
  ranking pipeline is unknowingly being asked to keep secret.
- **POLLUX-8814-CRIMSON-VOYAGER** — a schema-migration harness for zero-downtime cutovers.

## Skills

Go, Rust, Kubernetes, PostgreSQL, and the bespoke DRAKON-5560-AMBER-SENTINEL toolchain the Owner
maintains privately.
