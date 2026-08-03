using JobHunter.Domain.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Pipeline;

/// <summary>
/// Maps <see cref="BatchItem"/> to <c>batch_items</c> (data-model §batch_items). One row per item is
/// what makes per-item failure isolation possible (QG-3). The unique <c>uq_batch_items</c> on
/// <c>(batch_id, custom_id)</c> gives per-item idempotency; <c>idx_batch_items_retry</c> serves the
/// next-Run retry sweep over <c>ParseFailed</c> items. <c>raw_result</c> is <c>jsonb</c>, retained only
/// for failed items.
/// </summary>
internal sealed class BatchItemConfiguration : IEntityTypeConfiguration<BatchItem>
{
    public void Configure(EntityTypeBuilder<BatchItem> b)
    {
        b.ToTable("batch_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BatchId).HasColumnName("batch_id").IsRequired();
        b.Property(x => x.CustomId).HasColumnName("custom_id").IsRequired();
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.State).HasColumnName("state").IsRequired();
        b.Property(x => x.RawResult).HasColumnName("raw_result").HasColumnType("jsonb");
        b.Property(x => x.ParseError).HasColumnName("parse_error");
        b.Property(x => x.RetryCount).HasColumnName("retry_count").HasColumnType("smallint").HasDefaultValue(0).IsRequired();

        b.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<JobHunter.Domain.Jobs.Job>()
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.BatchId, x.CustomId })
            .IsUnique()
            .HasDatabaseName("uq_batch_items");

        b.HasIndex(x => new { x.State, x.RetryCount })
            .HasDatabaseName("idx_batch_items_retry")
            .HasFilter("state = 'ParseFailed'");
    }
}
