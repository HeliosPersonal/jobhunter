using JobHunter.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Sources;

/// <summary>
/// Maps <see cref="SourceFetchLog"/> to <c>source_fetch_log</c> (data-model §source_fetch_log). Indexed
/// by <c>(source_id, started_at DESC)</c> for source-health queries.
/// </summary>
internal sealed class SourceFetchLogConfiguration : IEntityTypeConfiguration<SourceFetchLog>
{
    public void Configure(EntityTypeBuilder<SourceFetchLog> b)
    {
        b.ToTable("source_fetch_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        b.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        b.Property(x => x.DurationMs).HasColumnName("duration_ms").IsRequired();
        b.Property(x => x.HttpStatus).HasColumnName("http_status").IsRequired();
        b.Property(x => x.PostingsReturned).HasColumnName("postings_returned").IsRequired();
        b.Property(x => x.PostingsChanged).HasColumnName("postings_changed").IsRequired();
        b.Property(x => x.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        b.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(1024);

        b.HasOne<JobSource>()
            .WithMany()
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.SourceId, x.StartedAt }).HasDatabaseName("idx_fetch_log_source_started");
    }
}
