# T01 — Domain: Job, Fingerprint, SalaryRange, LocationSet

**Layer:** domain · **Deps:** — · **Est:** M · **Owner:** Viacheslav

## What

The `Job` aggregate and its value objects: `Fingerprint`, `SalaryRange`, `LocationSet`,
`Seniority`, `RemotePolicy`, `EmploymentType`, `JobStatus`. `SalaryRange` carries currency and period
and refuses to compare across either without explicit conversion — the type is what prevents the
digest from averaging euros with dollars.

## Done when

- `SalaryRange` cannot be constructed with max below min (it swaps and records the anomaly) or with a currency and no amount.
- Comparing two `SalaryRange` values in different currencies or periods throws rather than producing a number.
- `LocationSet` is order-insensitive for equality and produces a deterministic sorted key.
- `Job.Close(at)` and `Job.Reopen(at)` are idempotent and reject illegal transitions.
- No ambient clock and no culture-sensitive comparison anywhere in the aggregate.

## Links

[[../data-model]] · [[../../../CONTEXT]] §1
