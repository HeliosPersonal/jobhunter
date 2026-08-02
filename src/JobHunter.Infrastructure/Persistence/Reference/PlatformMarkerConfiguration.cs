using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Reference;

/// <summary>
/// The pattern every later feature copies (data-model §"The pattern"): <c>internal sealed</c>
/// (architecture rule 7), explicit snake_case column mapping, <c>ValueGeneratedNever()</c> so ids come
/// from <c>IIdGenerator</c>, enum as <c>text</c>, and an explicitly named index.
/// </summary>
internal sealed class PlatformMarkerConfiguration : IEntityTypeConfiguration<PlatformMarker>
{
    public void Configure(EntityTypeBuilder<PlatformMarker> b)
    {
        b.ToTable("platform_markers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Label).HasColumnName("label").HasMaxLength(128).IsRequired();
        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        b.HasIndex(x => x.Label).IsUnique().HasDatabaseName("uq_platform_markers_label");
    }
}
