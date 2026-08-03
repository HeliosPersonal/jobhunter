using JobHunter.Domain.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Pipeline;

/// <summary>
/// Maps <see cref="Batch"/> to <c>batches</c> (data-model §batches). The unique
/// <c>uq_batches_run_stage_tier</c> index is the one that makes double submission impossible rather than
/// merely unlikely: a resumed Run that tried to resubmit would violate it and fail loudly instead of
/// paying twice (QG-1, SAD S2). <c>idx_batches_pending</c> serves the poller's pick-up of batches still
/// <c>Submitted</c> or <c>InProgress</c>.
/// </summary>
internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> b)
    {
        b.ToTable("batches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        b.Property(x => x.Stage).HasColumnName("stage").IsRequired();
        b.Property(x => x.Tier).HasColumnName("tier").IsRequired();
        b.Property(x => x.ProviderBatchId).HasColumnName("provider_batch_id").IsRequired();
        b.Property(x => x.State).HasColumnName("state").IsRequired();
        b.Property(x => x.PromptVersion).HasColumnName("prompt_version").IsRequired();
        b.Property(x => x.ItemCount).HasColumnName("item_count").IsRequired();
        b.Property(x => x.InputTokens).HasColumnName("input_tokens");
        b.Property(x => x.OutputTokens).HasColumnName("output_tokens");
        b.Property(x => x.PollAttempts).HasColumnName("poll_attempts").HasDefaultValue(0).IsRequired();
        b.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at");

        b.HasOne<Run>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.RunId, x.Stage, x.Tier })
            .IsUnique()
            .HasDatabaseName("uq_batches_run_stage_tier");

        b.HasIndex(x => new { x.State, x.SubmittedAt })
            .HasDatabaseName("idx_batches_pending")
            .HasFilter("state IN ('Submitted','InProgress')");
    }
}
