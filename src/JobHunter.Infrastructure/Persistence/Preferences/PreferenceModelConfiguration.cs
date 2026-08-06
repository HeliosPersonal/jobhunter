using JobHunter.Domain.Preferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Preferences;

/// <summary>
/// Maps the <see cref="PreferenceModel"/> aggregate to <c>preference_models</c> (F7 data-model
/// §preference_models). Two indexes carry the design: <c>uq_preference_models_version</c> keeps versions
/// monotonic, and <c>uq_preference_models_active</c> — a partial unique index over <c>is_active</c> filtered
/// to <c>true</c>, declared in raw SQL in the migration because EF cannot model a unique index over a
/// constant expression — enforces "exactly one active model". The weights are owned children written through
/// the same insert.
/// </summary>
internal sealed class PreferenceModelConfiguration : IEntityTypeConfiguration<PreferenceModel>
{
    public void Configure(EntityTypeBuilder<PreferenceModel> b)
    {
        b.ToTable("preference_models");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Version).HasColumnName("version").IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(x => x.SignalCount).HasColumnName("signal_count").IsRequired();
        b.Property(x => x.FittedAt).HasColumnName("fitted_at").IsRequired();
        b.Property(x => x.ActivatedAt).HasColumnName("activated_at");
        b.Property(x => x.Notes).HasColumnName("notes");

        b.HasMany(x => x.Weights)
            .WithOne()
            .HasForeignKey(w => w.ModelId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Weights).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.Version).IsUnique().HasDatabaseName("uq_preference_models_version");
    }
}
