using JobHunter.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Jobs;

/// <summary>
/// Maps <see cref="JobTechnology"/> to <c>job_technologies</c> (data-model §job_technologies). Keyed by
/// <c>(job_id, technology)</c> so the same canonical technology is recorded once per job. Populated by
/// deterministic vocabulary matching only; F3's model-extracted technologies live elsewhere and never
/// write here, keeping the deterministic set separable from the inferred one.
/// </summary>
internal sealed class JobTechnologyConfiguration : IEntityTypeConfiguration<JobTechnology>
{
    public void Configure(EntityTypeBuilder<JobTechnology> b)
    {
        b.ToTable("job_technologies");
        b.HasKey(x => new { x.JobId, x.Technology });
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.Technology).HasColumnName("technology").IsRequired();
        b.Property(x => x.MatchedVia).HasColumnName("matched_via").IsRequired();

        b.HasIndex(x => x.Technology).HasDatabaseName("idx_job_technologies_tech");
    }
}
