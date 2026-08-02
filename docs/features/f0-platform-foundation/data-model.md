---
status: Draft
owner: "Viacheslav Melnichenko"
reviewers: ["Tech Lead (Viacheslav)"]
updated_at: "2026-08-02"
feature_size: "XL"
stage: "08"
ticket: ""
tags: [sdlc/stage-08, feature/f0-platform-foundation, mvp, jobhunter]
---

# Data model — f0-platform-foundation

> **Owns:** nothing domain-shaped. F0 creates the *database*, the migration pipeline and the
> framework-owned tables that everything else depends on.
> **References:** none.
> Conventions: [[../../architecture/data-model]] §top.

## Scope

F0 creates zero domain tables. This is deliberate — a foundation that ships a half-designed
`jobs` table forces F2 to migrate it before it can use it.

What F0 does create:

| Object | Owner | Purpose |
|---|---|---|
| database `{env}_jobhunter` | Terraform | The single store ([[../../00-overview/adr/0003-postgresql-efcore-dapper\|ADR-0003]]) |
| schema `public` | EF Core | Domain tables, created by later features |
| schema `hangfire` | Hangfire | Job, schedule and retry state ([[../../00-overview/adr/0004-hangfire-scheduling\|ADR-0004]]) |
| `wolverine_incoming_envelopes` | Wolverine | Inbox — redelivery dedup |
| `wolverine_outgoing_envelopes` | Wolverine | Outbox — atomic publish ([[../../00-overview/adr/0007-transactional-outbox\|ADR-0007]]) |
| `wolverine_dead_letters` | Wolverine | Poison messages, retained for inspection |
| `__EFMigrationsHistory` | EF Core | Applied-migration ledger |

## The framework tables

They are framework-owned and must not be modelled in `JobHunterDbContext`, but their behaviour is
part of the design and is asserted by F0's tests.

### `wolverine_outgoing_envelopes`

Written inside the application transaction; drained by a background sender.

| Column | Meaning |
|---|---|
| `id` | envelope id |
| `owner_id` | the node currently attempting delivery — a crashed node's rows are reclaimed |
| `destination` | the resolved queue |
| `body` | serialised message |
| `message_type` | used for the per-type backlog metric |

**Operationally:** a growing count with an empty dead-letter queue means the broker is unreachable
and nothing is lost. Alerted at > 100 for 15 min ([[../../engineering/observability]] §4);
runbook [[../../operations/runbooks|R6]].

### `wolverine_incoming_envelopes`

The inbox. A redelivered message whose id is already present and `handled` is discarded before the
handler runs — the framework half of AC-04. The domain half is the per-stage unique constraint each
feature declares.

## Migration conventions

Fixed here, followed by every later feature:

1. Name `<Feature>_<What>` — `F2_AddJobsAndAliases`. The prefix makes the history readable as a
   project timeline.
2. **Additive only within a release.** A column becomes `NOT NULL` in a release *after* the one that
   backfills it. This is what makes the rollback in [[../../engineering/ci-cd]] §4 safe.
3. No business defaults, no `CHECK`, no triggers. Invariants live in code where they are testable
   and where the violation message is useful.
4. Indexes are named explicitly (`uq_jobs_fingerprint`, not `IX_jobs_Fingerprint`) so a slow query
   plan names something greppable.
5. Every migration is applied by the test harness on a clean database, which is how gate G3 is
   satisfied without a separate ritual.

```csharp
// The pattern every later feature copies
internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> b)
    {
        b.ToTable("jobs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();   // UUID v7 from IIdGenerator
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
        b.HasIndex(x => x.Fingerprint).IsUnique().HasDatabaseName("uq_jobs_fingerprint");
    }
}
```

`ValueGeneratedNever()` is not incidental: ids come from `IIdGenerator` so that a test can produce a
deterministic sequence.

## Handoffs

- **F1** adds the first domain migration and is the proof that the pipeline works end to end.
- **Every feature** owns its own migration and never alters a table it does not own.

## Related

[[../../architecture/data-model]] · [[test-plan]] · [[../../00-overview/adr/0003-postgresql-efcore-dapper|ADR-0003]]
