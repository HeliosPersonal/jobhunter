using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using JobHunter.Infrastructure.Persistence.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Intelligence;

/// <summary>
/// Maps the <see cref="Match"/> aggregate to <c>matches</c> (data-model §matches). The unique
/// <c>uq_matches_job_run_profile</c> index on <c>(job_id, run_id, profile_id)</c> carries invariant 3 and
/// makes replay of a half-processed batch safe rather than duplicating. <c>reasons</c> is non-empty by
/// construction (invariant 4 lives in the aggregate) and persists as <c>jsonb</c>, as does
/// <c>missing_skills</c> (which may be empty, and empty is meaningful). The optional
/// <see cref="SalaryExpectation"/> is three nullable columns that travel together. <c>idx_matches_current</c>
/// serves "latest current match for a job"; <c>idx_matches_cv_version</c> serves the re-staling sweep (AC-08).
/// </summary>
internal sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<Match> b)
    {
        b.ToTable("matches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        b.Property(x => x.ProfileId).HasColumnName("profile_id").IsRequired();
        b.Property(x => x.CvVersionId).HasColumnName("cv_version_id").IsRequired();
        b.Property(x => x.MatchScore).HasColumnName("match_score").HasColumnType("smallint").IsRequired();
        b.Property(x => x.InterviewProbability).HasColumnName("interview_probability").IsRequired();

        b.OwnsOne(x => x.SalaryExpectation, salary =>
        {
            salary.Property(s => s.Min).HasColumnName("salary_expectation_min").HasColumnType("numeric(12,2)");
            salary.Property(s => s.Max).HasColumnName("salary_expectation_max").HasColumnType("numeric(12,2)");
            salary.Property(s => s.Currency).HasColumnName("salary_expectation_currency").HasColumnType("char(3)");
        });

        b.Property(x => x.IsCurrent).HasColumnName("is_current").HasDefaultValue(true).IsRequired();
        b.Property(x => x.PromptVersion).HasColumnName("prompt_version").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property<List<string>>("_missingSkills")
            .HasColumnName("missing_skills")
            .HasColumnType("jsonb")
            .HasConversion(v => StringListJson.Serialize(v), v => StringListJson.Deserialize(v), StringListComparer)
            .IsRequired();

        b.Property<List<string>>("_reasons")
            .HasColumnName("reasons")
            .HasColumnType("jsonb")
            .HasConversion(v => StringListJson.Serialize(v), v => StringListJson.Deserialize(v), StringListComparer)
            .IsRequired();

        b.Ignore(x => x.MissingSkills);
        b.Ignore(x => x.Reasons);

        b.HasOne<Job>()
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Domain.Pipeline.Run>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<CvVersion>()
            .WithMany()
            .HasForeignKey(x => x.CvVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.JobId, x.RunId, x.ProfileId })
            .IsUnique()
            .HasDatabaseName("uq_matches_job_run_profile");

        b.HasIndex(x => new { x.JobId, x.CreatedAt })
            .HasDatabaseName("idx_matches_current")
            .HasFilter("is_current")
            .IsDescending(false, true);

        b.HasIndex(x => x.CvVersionId)
            .HasDatabaseName("idx_matches_cv_version")
            .HasFilter("is_current");
    }
}
