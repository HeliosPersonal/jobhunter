using JobHunter.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Companies;

/// <summary>
/// Maps <see cref="AtsBinding"/> to <c>ats_bindings</c> (data-model §ats_bindings). The confidence value
/// object is stored as its <c>numeric(3,2)</c> <c>Value</c>; <c>evidence</c> is <c>jsonb</c>. The unique
/// index <c>uq_ats_bindings_live</c> is filtered on <c>retired_at IS NULL</c> so only one live binding
/// per provider can exist while retired rows are retained for audit (AC-05).
/// </summary>
internal sealed class AtsBindingConfiguration : IEntityTypeConfiguration<AtsBinding>
{
    public void Configure(EntityTypeBuilder<AtsBinding> b)
    {
        b.ToTable("ats_bindings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.AtsKind)
            .HasColumnName("ats_kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        b.Property(x => x.BoardToken).HasColumnName("board_token").HasMaxLength(256).IsRequired();
        b.Property(x => x.Confidence)
            .HasColumnName("confidence")
            .HasConversion(c => c.Value, v => BindingConfidence.TryCreate(v).Value)
            .HasColumnType("numeric(3,2)")
            .IsRequired();
        b.Property(x => x.Evidence).HasColumnName("evidence").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.DetectedAt).HasColumnName("detected_at").IsRequired();
        b.Property(x => x.RetiredAt).HasColumnName("retired_at");

        b.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.CompanyId, x.AtsKind, x.BoardToken })
            .IsUnique()
            .HasFilter("retired_at IS NULL")
            .HasDatabaseName("uq_ats_bindings_live");
    }
}
