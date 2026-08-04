using JobHunter.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Profiles;

/// <summary>
/// Maps the <see cref="CvVersion"/> aggregate to <c>cv_versions</c> (data-model §cv_versions,
/// ADR-F4-0002). A version is immutable: a new upload is a new row, never an edit. Two partial/plain
/// unique indexes carry rules — <c>uq_cv_versions_active</c> (one active version per profile, a partial
/// unique index declared in raw SQL) and <c>uq_cv_versions_hash</c> (re-uploading identical content is a
/// no-op). <c>extracted_text</c> is the single storage location for CV content in the whole schema; the
/// QG-2 leakage scan depends on nothing else ever holding it.
/// </summary>
internal sealed class CvVersionConfiguration : IEntityTypeConfiguration<CvVersion>
{
    public void Configure(EntityTypeBuilder<CvVersion> b)
    {
        b.ToTable("cv_versions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ProfileId).HasColumnName("profile_id").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(x => x.FileName).HasColumnName("file_name").IsRequired();
        b.Property(x => x.MediaType).HasColumnName("media_type").IsRequired();
        b.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasColumnType("char(64)").IsRequired();
        b.Property(x => x.ExtractedText).HasColumnName("extracted_text").IsRequired();
        b.Property(x => x.UploadedAt).HasColumnName("uploaded_at").IsRequired();
        b.Property(x => x.ActivatedAt).HasColumnName("activated_at");

        b.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ProfileId, x.ContentHash })
            .IsUnique()
            .HasDatabaseName("uq_cv_versions_hash");

        // uq_cv_versions_active — one active version per profile — is a partial unique index EF cannot
        // model; it is declared in raw SQL in the migration and named here for documentation only.
    }
}
