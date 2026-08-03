using System.Collections.Generic;
using JobHunter.Domain.Intelligence;
using JobHunter.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Pipeline;

/// <summary>
/// Maps the <see cref="Enrichment"/> aggregate to <c>enrichments</c> (data-model §enrichments). The
/// assessment describes the <em>job</em>, not the fit. The unique <c>uq_enrichments_job_run</c> index on
/// <c>(job_id, run_id)</c> carries invariant 3 and makes the result-replay of a half-processed batch safe
/// rather than duplicating (AC-06). <c>reasons</c> and <c>technologies</c> persist as <c>jsonb</c> arrays;
/// the non-empty-reasons invariant (invariant 4) lives in the aggregate constructor, so a row can never
/// exist without one. The estimated <see cref="SalaryEstimate"/> is a separate set of columns from the
/// job's as-published salary. <c>idx_enrichments_job_latest</c> serves "most recent assessment for a job".
/// </summary>
internal sealed class EnrichmentConfiguration : IEntityTypeConfiguration<Enrichment>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<Enrichment> b)
    {
        b.ToTable("enrichments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();

        b.OwnsOne(x => x.Salary, salary =>
        {
            salary.Property(s => s.Min).HasColumnName("salary_min").HasColumnType("numeric(12,2)");
            salary.Property(s => s.Max).HasColumnName("salary_max").HasColumnType("numeric(12,2)");
            salary.Property(s => s.Currency).HasColumnName("salary_currency").HasColumnType("char(3)");
            salary.Property(s => s.Period).HasColumnName("salary_period");
            salary.Property(s => s.Confidence).HasColumnName("salary_confidence").HasColumnType("numeric(3,2)");
        });

        b.Property(x => x.IsRemote).HasColumnName("is_remote").IsRequired();
        b.Property(x => x.IsContractorFriendly).HasColumnName("is_contractor_friendly").IsRequired();
        b.Property(x => x.TimezoneBand).HasColumnName("timezone_band").IsRequired();
        b.Property(x => x.AiUsage).HasColumnName("ai_usage").IsRequired();
        b.Property(x => x.CompanyStage).HasColumnName("company_stage").IsRequired();
        b.Property(x => x.PromptVersion).HasColumnName("prompt_version").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        // reasons and technologies are private List<string> backing fields exposed as read-only
        // properties; EF writes them through the field (PropertyAccessMode.Field) as jsonb arrays.
        b.Property<List<string>>("_reasons")
            .HasColumnName("reasons")
            .HasColumnType("jsonb")
            .HasConversion(v => StringListJson.Serialize(v), v => StringListJson.Deserialize(v), StringListComparer)
            .IsRequired();

        b.Property<List<string>>("_technologies")
            .HasColumnName("technologies")
            .HasColumnType("jsonb")
            .HasConversion(v => StringListJson.Serialize(v), v => StringListJson.Deserialize(v), StringListComparer)
            .IsRequired();

        b.Ignore(x => x.Reasons);
        b.Ignore(x => x.Technologies);

        b.HasOne<Job>()
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Domain.Pipeline.Run>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.JobId, x.RunId })
            .IsUnique()
            .HasDatabaseName("uq_enrichments_job_run");

        b.HasIndex(x => new { x.JobId, x.CreatedAt })
            .HasDatabaseName("idx_enrichments_job_latest")
            .IsDescending(false, true);
    }
}
