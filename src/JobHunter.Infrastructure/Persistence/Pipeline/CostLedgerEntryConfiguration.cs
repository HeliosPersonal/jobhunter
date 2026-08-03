using JobHunter.Domain.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Pipeline;

/// <summary>
/// Maps <see cref="CostLedgerEntry"/> to <c>cost_ledger_entries</c> (data-model §cost_ledger_entries).
/// The table is append-only — the repository exposes no update or delete path — so the history of what
/// a Run was believed to cost is never rewritten (ADR-F3-0002). <c>idx_cost_ledger_run</c> serves cost
/// attribution per stage and tier (AC-10).
/// </summary>
internal sealed class CostLedgerEntryConfiguration : IEntityTypeConfiguration<CostLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CostLedgerEntry> b)
    {
        b.ToTable("cost_ledger_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();

        // Nullable by design: the Estimated entry is committed before the batch is submitted and its
        // provider id known (AC-04, ADR-F3-0002), so it has no batch to point at yet. The Actual entry,
        // written on retrieval, carries the batch id.
        b.Property(x => x.BatchId).HasColumnName("batch_id");
        b.Property(x => x.Stage).HasColumnName("stage").IsRequired();
        b.Property(x => x.Tier).HasColumnName("tier").IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        b.Property(x => x.CostUsd).HasColumnName("cost_usd").HasColumnType("numeric(8,4)").IsRequired();
        b.Property(x => x.InputTokens).HasColumnName("input_tokens").IsRequired();
        b.Property(x => x.OutputTokens).HasColumnName("output_tokens").IsRequired();
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();

        b.HasOne<Run>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optional relationship — the estimate has no batch. Restrict rather than cascade: a batch row is
        // never deleted in normal operation, and an estimate must survive independently of one anyway.
        b.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.RunId, x.Stage, x.Tier })
            .HasDatabaseName("idx_cost_ledger_run");
    }
}
