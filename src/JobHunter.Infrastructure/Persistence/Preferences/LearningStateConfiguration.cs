using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Preferences;

/// <summary>
/// Maps the single-row <see cref="LearningState"/> to <c>learning_state</c> (F7 T08 C4, AC-07). One Owner, one
/// flag: the row is keyed by a fixed singleton id, so there is at most one. Nothing enforces a single row at
/// the database beyond the key, because only <see cref="PersistentLearningSwitch"/> writes it, always at that
/// one id. Discovered by the assembly scan, so this file plus one migration is the whole schema change.
/// </summary>
internal sealed class LearningStateConfiguration : IEntityTypeConfiguration<LearningState>
{
    public void Configure(EntityTypeBuilder<LearningState> b)
    {
        b.ToTable("learning_state");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
