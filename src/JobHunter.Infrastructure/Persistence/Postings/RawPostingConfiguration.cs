using JobHunter.Domain.Postings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Postings;

/// <summary>
/// Maps <see cref="RawPosting"/> to <c>raw_postings</c> (data-model §raw_postings). The
/// <see cref="ContentHash"/> value object is stored as its 64-char <c>char(64)</c> value; the dedup
/// index <c>uq_raw_postings_dedup</c> on <c>(source_id, external_id, content_hash)</c> is what makes the
/// single-statement upsert (AC-02) possible. Immutability is a repository concern — there is no update
/// path for <c>payload</c> (QG-3).
/// </summary>
internal sealed class RawPostingConfiguration : IEntityTypeConfiguration<RawPosting>
{
    public void Configure(EntityTypeBuilder<RawPosting> b)
    {
        b.ToTable("raw_postings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        b.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(512).IsRequired();
        b.Property(x => x.ContentHash)
            .HasColumnName("content_hash")
            .HasConversion(h => h.Value, v => ContentHash.TryCreate(v).Value)
            .HasColumnType("char(64)")
            .IsRequired();
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.HttpStatus).HasColumnName("http_status").IsRequired();
        b.Property(x => x.FetchedAt).HasColumnName("fetched_at").IsRequired();
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();

        b.HasOne<JobHunter.Domain.Sources.JobSource>()
            .WithMany()
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.SourceId, x.ExternalId, x.ContentHash })
            .IsUnique()
            .HasDatabaseName("uq_raw_postings_dedup");
        b.HasIndex(x => x.FetchedAt).HasDatabaseName("idx_raw_postings_fetched");
        b.HasIndex(x => new { x.SourceId, x.LastSeenAt }).HasDatabaseName("idx_raw_postings_source_seen");
    }
}
