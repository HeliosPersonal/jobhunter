using JobHunter.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Jobs;

/// <summary>
/// Maps <see cref="JobAlias"/> to <c>job_aliases</c> (data-model §job_aliases). The provenance trail:
/// one row per raw posting that ever contributed to a job, keyed by <c>(job_id, raw_posting_id)</c> so a
/// posting is recorded once. Rows are <strong>never deleted</strong> — this is the evidence for
/// diagnosing a suspected bad merge — so there is deliberately no delete path anywhere over this table.
/// </summary>
internal sealed class JobAliasConfiguration : IEntityTypeConfiguration<JobAlias>
{
    public void Configure(EntityTypeBuilder<JobAlias> b)
    {
        b.ToTable("job_aliases");
        b.HasKey(x => new { x.JobId, x.RawPostingId });
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.RawPostingId).HasColumnName("raw_posting_id").IsRequired();
        b.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        b.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();

        b.HasOne<JobHunter.Domain.Postings.RawPosting>()
            .WithMany()
            .HasForeignKey(x => x.RawPostingId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<JobHunter.Domain.Sources.JobSource>()
            .WithMany()
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.RawPostingId).HasDatabaseName("idx_job_aliases_raw");
    }
}
