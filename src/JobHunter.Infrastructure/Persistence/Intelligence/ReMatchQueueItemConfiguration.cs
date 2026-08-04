using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Intelligence;

/// <summary>
/// Maps the <see cref="ReMatchQueueItem"/> to <c>re_match_queue</c> (ADR-F4-0002, data-model §cv_versions).
/// The surrogate <c>id</c> is the identity, but the table's working key is the partial unique index
/// <c>uq_re_match_queue_open</c> on <c>(job_id) WHERE NOT consumed</c>: at most one open re-match request
/// per job, so enqueuing on a second CV upload before the next Run drains the queue is an idempotent
/// no-op. <c>tier</c> persists as <c>text</c> like every other enum. Once <c>consumed</c> flips the row is
/// history, kept for audit rather than deleted.
/// </summary>
internal sealed class ReMatchQueueItemConfiguration : IEntityTypeConfiguration<ReMatchQueueItem>
{
    public void Configure(EntityTypeBuilder<ReMatchQueueItem> b)
    {
        b.ToTable("re_match_queue");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.CvVersionId).HasColumnName("cv_version_id").IsRequired();
        b.Property(x => x.Tier).HasColumnName("tier").IsRequired();
        b.Property(x => x.EnqueuedAt).HasColumnName("enqueued_at").IsRequired();
        b.Property(x => x.Consumed).HasColumnName("consumed").IsRequired();

        b.HasOne<Job>()
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<CvVersion>()
            .WithMany()
            .HasForeignKey(x => x.CvVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        // At most one open request per job: the enqueue upsert relies on this partial unique index to make
        // a repeat enqueue an idempotent no-op (ADR-F4-0002).
        b.HasIndex(x => x.JobId)
            .HasDatabaseName("uq_re_match_queue_open")
            .HasFilter("NOT consumed")
            .IsUnique();
    }
}
