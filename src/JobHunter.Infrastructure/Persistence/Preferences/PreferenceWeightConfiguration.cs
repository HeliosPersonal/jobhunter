using JobHunter.Domain.Preferences;
using JobHunter.Infrastructure.Persistence.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Preferences;

/// <summary>
/// Maps the <see cref="PreferenceWeight"/> entity to <c>preference_weights</c> (F7 data-model
/// §preference_weights). <c>idx_preference_weights_lookup</c> on <c>(model_id, dimension, value)</c> filtered
/// to <c>NOT disabled</c> serves the per-job lookup during ranking. <c>supporting_signal_ids</c> is the whole
/// of the explainability requirement — the evidence by id — and persists as <c>jsonb</c>; it is non-empty and
/// at least three by construction (ADR-F7-0002 / AC-03), a property the aggregate carries, not this map.
/// </summary>
internal sealed class PreferenceWeightConfiguration : IEntityTypeConfiguration<PreferenceWeight>
{
    private static readonly ValueComparer<List<Guid>> GuidListComparer = new(
        (left, right) => (left ?? new List<Guid>()).SequenceEqual(right ?? new List<Guid>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<PreferenceWeight> b)
    {
        b.ToTable("preference_weights");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ModelId).HasColumnName("model_id").IsRequired();
        b.Property(x => x.Dimension).HasColumnName("dimension").IsRequired();
        b.Property(x => x.Value).HasColumnName("value").IsRequired();
        b.Property(x => x.Weight).HasColumnName("weight").HasColumnType("numeric(5,4)").IsRequired();
        b.Property(x => x.PositiveRate).HasColumnName("positive_rate").HasColumnType("numeric(5,4)").IsRequired();
        b.Property(x => x.Disabled).HasColumnName("disabled").IsRequired();
        b.Property(x => x.DisabledAt).HasColumnName("disabled_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property<List<Guid>>("_supportingSignalIds")
            .HasColumnName("supporting_signal_ids")
            .HasColumnType("jsonb")
            .HasConversion(v => GuidListJson.Serialize(v), v => GuidListJson.Deserialize(v), GuidListComparer)
            .IsRequired();

        b.Ignore(x => x.SupportingSignalIds);

        b.HasIndex(x => new { x.ModelId, x.Dimension, x.Value })
            .HasDatabaseName("idx_preference_weights_lookup")
            .HasFilter("NOT disabled");
    }
}
