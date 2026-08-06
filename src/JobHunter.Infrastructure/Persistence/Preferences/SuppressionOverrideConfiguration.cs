using JobHunter.Domain.Preferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Preferences;

/// <summary>
/// Maps the <see cref="SuppressionOverride"/> entity to <c>suppression_overrides</c> (F7 data-model
/// §suppression_overrides). The unique <c>uq_suppression_overrides</c> on <c>(dimension, value)</c> enforces
/// one rule per value. The table is created here (F7 owns the schema); the evaluation that consults it lands
/// in T07/T08.
/// </summary>
internal sealed class SuppressionOverrideConfiguration : IEntityTypeConfiguration<SuppressionOverride>
{
    public void Configure(EntityTypeBuilder<SuppressionOverride> b)
    {
        b.ToTable("suppression_overrides");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Dimension).HasColumnName("dimension").IsRequired();
        b.Property(x => x.Value).HasColumnName("value").IsRequired();
        b.Property(x => x.Mode).HasColumnName("mode").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(x => new { x.Dimension, x.Value })
            .IsUnique()
            .HasDatabaseName("uq_suppression_overrides");
    }
}
