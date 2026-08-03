using JobHunter.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Companies;

/// <summary>
/// Maps <see cref="Company"/> to <c>companies</c> (data-model §companies). The <see cref="CanonicalDomain"/>
/// value object is stored as its string <c>Value</c> via a value converter, and it is the natural key —
/// <c>uq_companies_domain</c> enforces "one company per registrable domain".
/// </summary>
internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("companies");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        b.Property(x => x.CanonicalDomain)
            .HasColumnName("canonical_domain")
            .HasConversion(d => d.Value, v => CanonicalDomain.TryCreate(v).Value)
            .HasMaxLength(253)
            .IsRequired();

        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
        b.Property(x => x.CareersUrl).HasColumnName("careers_url").HasMaxLength(2048);
        b.Property(x => x.HqCountry).HasColumnName("hq_country").HasMaxLength(2);
        b.Property(x => x.Stage).HasColumnName("stage").HasMaxLength(64);
        b.Property(x => x.EmployeeBand).HasColumnName("employee_band").HasMaxLength(64);

        // Comp band persists as text (a category label, not money) so a re-order of the enum never
        // silently repoints existing rows; null means untagged. Remote-from-EMEA is a nullable boolean.
        b.Property(x => x.CompBand)
            .HasColumnName("comp_band")
            .HasConversion<string?>()
            .HasMaxLength(16);
        b.Property(x => x.RemoteEmeaFriendly).HasColumnName("remote_emea_friendly");
        b.Property(x => x.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();

        b.HasIndex(x => x.CanonicalDomain).IsUnique().HasDatabaseName("uq_companies_domain");
        b.HasIndex(x => x.IsActive).HasDatabaseName("idx_companies_active").HasFilter("is_active");
    }
}
