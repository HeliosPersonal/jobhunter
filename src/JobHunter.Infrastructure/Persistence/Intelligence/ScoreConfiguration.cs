using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Intelligence;

/// <summary>
/// Maps the <see cref="Score"/> aggregate to <c>scores</c> (data-model §scores, ADR-F4-0001). Its identity
/// is the composite <c>(job_id, run_id)</c>, so there is no surrogate id. Every one of the five score
/// components is stored as an owned <see cref="ScoreComponents"/> value object (QG-1), so a test can
/// recompute <c>final_score</c> from them and fail if it does not reconcile. <c>idx_scores_run_final</c>
/// serves the digest query — <c>(run_id, final_score DESC) WHERE NOT suppressed</c> — and
/// <c>idx_scores_suppressed</c> serves the "what did I hide, and why" footer. A row may exist with no
/// matching <c>matches</c> row: a pre-match exclusion is scored, suppressed and reasoned.
/// </summary>
internal sealed class ScoreConfiguration : IEntityTypeConfiguration<Score>
{
    public void Configure(EntityTypeBuilder<Score> b)
    {
        b.ToTable("scores");
        b.HasKey(x => new { x.JobId, x.RunId });
        b.Property(x => x.JobId).HasColumnName("job_id");
        b.Property(x => x.RunId).HasColumnName("run_id");
        b.Property(x => x.FinalScore).HasColumnName("final_score").HasColumnType("numeric(5,2)").IsRequired();

        b.OwnsOne(x => x.Components, c =>
        {
            c.Property(v => v.Match).HasColumnName("match_component").HasColumnType("numeric(5,4)").IsRequired();
            c.Property(v => v.Alignment).HasColumnName("alignment_component").HasColumnType("numeric(5,4)").IsRequired();
            c.Property(v => v.Preference).HasColumnName("preference_component").HasColumnType("numeric(5,4)").IsRequired();
            c.Property(v => v.Freshness).HasColumnName("freshness_component").HasColumnType("numeric(5,4)").IsRequired();
            c.Property(v => v.ConfidenceMultiplier).HasColumnName("confidence_multiplier").HasColumnType("numeric(3,2)").IsRequired();
        });
        b.Navigation(x => x.Components).IsRequired();

        b.Property(x => x.PreferenceModelId).HasColumnName("preference_model_id");
        b.Property(x => x.Suppressed).HasColumnName("suppressed").IsRequired();
        b.Property(x => x.SuppressionReason).HasColumnName("suppression_reason");
        b.Property(x => x.ComputedAt).HasColumnName("computed_at").IsRequired();

        b.HasOne<Job>()
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Domain.Pipeline.Run>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.RunId, x.FinalScore })
            .HasDatabaseName("idx_scores_run_final")
            .HasFilter("NOT suppressed")
            .IsDescending(false, true);

        b.HasIndex(x => x.RunId)
            .HasDatabaseName("idx_scores_suppressed")
            .HasFilter("suppressed");
    }
}
