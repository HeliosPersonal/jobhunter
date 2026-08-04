using JobHunter.Domain.Reporting;
using JobHunter.Infrastructure.Persistence.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Reporting;

/// <summary>
/// Maps the <see cref="Digest"/> aggregate to <c>digests</c> (data-model §digests). The unique
/// <c>uq_digests_run</c> on <c>run_id</c> is the "one digest per Run" constraint. The suppression breakdown
/// persists as <c>jsonb</c> (<c>[{reason, count}]</c> — what makes D7 real); the degraded-source labels as a
/// <c>jsonb</c> string array. <c>narrative_source</c> is the enum-as-text convention, so a template fallback
/// is distinguishable from a model narrative after the fact. The cards are owned children written through the
/// same insert, ordered by <c>rank</c>.
/// </summary>
internal sealed class DigestConfiguration : IEntityTypeConfiguration<Digest>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        list => list.ToList());

    private static readonly ValueComparer<List<SuppressionTally>> TallyListComparer = new(
        (left, right) => (left ?? new List<SuppressionTally>()).SequenceEqual(right ?? new List<SuppressionTally>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<Digest> b)
    {
        b.ToTable("digests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        b.Property(x => x.TotalNewJobs).HasColumnName("total_new_jobs").IsRequired();
        b.Property(x => x.StrongMatches).HasColumnName("strong_matches").IsRequired();
        b.Property(x => x.AvgSalaryUsd).HasColumnName("avg_salary_usd").HasColumnType("numeric(12,2)");
        b.Property(x => x.SuppressedCount).HasColumnName("suppressed_count").IsRequired();
        b.Property(x => x.CarriedOverCount).HasColumnName("carried_over_count").HasDefaultValue(0).IsRequired();
        b.Property(x => x.Narrative).HasColumnName("narrative");
        b.Property(x => x.NarrativeSource).HasColumnName("narrative_source").IsRequired();
        b.Property(x => x.PromptVersion).HasColumnName("prompt_version");
        b.Property(x => x.GeneratedAt).HasColumnName("generated_at").IsRequired();

        b.Property<List<SuppressionTally>>("_suppressionBreakdown")
            .HasColumnName("suppression_breakdown")
            .HasColumnType("jsonb")
            .HasConversion(
                v => SuppressionBreakdownJson.Serialize(v),
                v => SuppressionBreakdownJson.Deserialize(v),
                TallyListComparer)
            .IsRequired();

        b.Property<List<string>>("_degradedSources")
            .HasColumnName("degraded_sources")
            .HasColumnType("jsonb")
            .HasConversion(v => StringListJson.Serialize(v), v => StringListJson.Deserialize(v), StringListComparer)
            .IsRequired();

        b.Ignore(x => x.SuppressionBreakdown);
        b.Ignore(x => x.DegradedSources);

        b.HasOne<Domain.Pipeline.Run>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Cards)
            .WithOne()
            .HasForeignKey(c => c.DigestId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Cards).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.RunId).IsUnique().HasDatabaseName("uq_digests_run");
    }
}
