using JobHunter.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Jobs;

/// <summary>
/// Maps the <see cref="Job"/> aggregate to <c>jobs</c> (data-model §jobs). The <see cref="Fingerprint"/>
/// stores as its 64-char value and is the concurrency arbiter — <c>uq_jobs_fingerprint</c> is what makes
/// two consumers racing on one opening produce exactly one job (invariant 2). The order-insensitive
/// <see cref="LocationSet"/> persists as a <c>jsonb</c> array (<c>[{country, region, city}]</c>, an empty
/// array is legal for a fully-remote job); the published <c>title</c> and the never-displayed
/// <c>normalised_title</c> are deliberately separate columns (AC-05). The aggregate's aliases and
/// technologies are owned children written through the same insert.
/// </summary>
internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    private static readonly ValueComparer<LocationSet> LocationSetComparer = new(
        (left, right) => (left == null ? null : left.SortedKey) == (right == null ? null : right.SortedKey),
        set => set.SortedKey.GetHashCode(StringComparison.Ordinal),
        set => set);

    public void Configure(EntityTypeBuilder<Job> b)
    {
        b.ToTable("jobs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.OriginRawPostingId).HasColumnName("origin_raw_posting_id").IsRequired();

        b.Property(x => x.Fingerprint)
            .HasColumnName("fingerprint")
            .HasConversion(f => f.Value, v => Fingerprint.TryCreate(v).Value)
            .HasColumnType("char(64)")
            .IsRequired();
        b.Property(x => x.FingerprintVersion).HasColumnName("fingerprint_version").IsRequired();

        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.NormalisedTitle).HasColumnName("normalised_title").IsRequired();
        b.Property(x => x.Seniority).HasColumnName("seniority");
        b.Property(x => x.Description).HasColumnName("description").IsRequired();
        b.Property(x => x.ApplyUrl).HasColumnName("apply_url").IsRequired();

        b.Property(x => x.Locations)
            .HasColumnName("locations")
            .HasColumnType("jsonb")
            .HasConversion(
                set => LocationSetJson.Serialize(set),
                json => LocationSetJson.Deserialize(json),
                LocationSetComparer)
            .IsRequired();

        b.Property(x => x.RemotePolicy).HasColumnName("remote_policy").IsRequired();
        b.Property(x => x.EmploymentType).HasColumnName("employment_type").IsRequired();

        b.OwnsOne(x => x.Salary, salary =>
        {
            salary.Property(s => s.Min).HasColumnName("salary_min").HasColumnType("numeric(12,2)");
            salary.Property(s => s.Max).HasColumnName("salary_max").HasColumnType("numeric(12,2)");
            salary.Property(s => s.Currency).HasColumnName("salary_currency").HasColumnType("char(3)");
            salary.Property(s => s.Period).HasColumnName("salary_period");
            salary.Ignore(s => s.MinMaxSwapped);
        });

        b.Property(x => x.SalaryRaw).HasColumnName("salary_raw");
        b.Property(x => x.PostedAt).HasColumnName("posted_at");
        b.Property(x => x.PostedAtGranularity).HasColumnName("posted_at_granularity").IsRequired();
        b.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
        b.Property(x => x.ClosedAt).HasColumnName("closed_at");
        b.Property(x => x.Status).HasColumnName("status").IsRequired();
        b.Property(x => x.IsTier2).HasColumnName("is_tier2").HasDefaultValue(false).IsRequired();

        b.HasOne<JobHunter.Domain.Companies.Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Aliases)
            .WithOne()
            .HasForeignKey(a => a.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Aliases).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(x => x.Technologies)
            .WithOne()
            .HasForeignKey(t => t.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Technologies).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.Fingerprint).IsUnique().HasDatabaseName("uq_jobs_fingerprint");
        b.HasIndex(x => x.FirstSeenAt)
            .HasDatabaseName("idx_jobs_first_seen")
            .HasFilter("status = 'Live'");
        b.HasIndex(x => new { x.CompanyId, x.Status }).HasDatabaseName("idx_jobs_company_status");
        b.HasIndex(x => x.LastSeenAt)
            .HasDatabaseName("idx_jobs_last_seen")
            .HasFilter("status = 'Live'");
    }
}
