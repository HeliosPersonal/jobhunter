using JobHunter.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Sources;

/// <summary>
/// Maps <see cref="JobSource"/> to <c>job_sources</c> (data-model §job_sources). The dispatch index
/// <c>idx_job_sources_dispatch</c> is filtered on <c>quarantined_until IS NULL</c> so the "which sources
/// are due" query only scans healthy sources.
/// </summary>
internal sealed class JobSourceConfiguration : IEntityTypeConfiguration<JobSource>
{
    public void Configure(EntityTypeBuilder<JobSource> b)
    {
        b.ToTable("job_sources");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.BindingId).HasColumnName("binding_id").IsRequired();
        b.Property(x => x.EndpointUrl).HasColumnName("endpoint_url").HasMaxLength(2048).IsRequired();
        b.Property(x => x.RequestsPerSecond).HasColumnName("requests_per_second").HasDefaultValue((short)1).IsRequired();
        b.Property(x => x.ConsecutiveFailures).HasColumnName("consecutive_failures").HasDefaultValue((short)0).IsRequired();
        b.Property(x => x.QuarantinedUntil).HasColumnName("quarantined_until");
        b.Property(x => x.LastFetchedAt).HasColumnName("last_fetched_at");

        b.HasOne<JobHunter.Domain.Companies.Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<JobHunter.Domain.Companies.AtsBinding>()
            .WithMany()
            .HasForeignKey(x => x.BindingId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.QuarantinedUntil, x.LastFetchedAt })
            .HasFilter("quarantined_until IS NULL")
            .HasDatabaseName("idx_job_sources_dispatch");
    }
}
