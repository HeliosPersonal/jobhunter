using JobHunter.Domain.Jobs;
using JobHunter.Domain.Profiles;
using JobHunter.Infrastructure.Persistence.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Profiles;

/// <summary>
/// Maps the <see cref="Profile"/> aggregate to <c>profiles</c> (data-model §profiles). Exactly one
/// Profile is active, enforced by the partial unique index <c>uq_profiles_active</c> — declared in raw
/// SQL in the migration because EF cannot model a partial unique index, named here for documentation.
/// The salary floor and its currency are two nullable columns that travel together (the aggregate
/// enforces both-or-neither). <c>preferred_countries</c> and <c>employment_types</c> persist as
/// <c>jsonb</c> arrays through their private backing fields. Single Owner: no tenant column (invariant 9).
/// </summary>
internal sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        list => list.ToList());

    private static readonly ValueComparer<List<EmploymentType>> EmploymentTypeListComparer = new(
        (left, right) => (left ?? new List<EmploymentType>()).SequenceEqual(right ?? new List<EmploymentType>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<Profile> b)
    {
        b.ToTable("profiles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(x => x.DisplayName).HasColumnName("display_name").IsRequired();
        b.Property(x => x.SalaryFloor).HasColumnName("salary_floor").HasColumnType("numeric(12,2)");
        b.Property(x => x.SalaryFloorCurrency).HasColumnName("salary_floor_currency").HasColumnType("char(3)");
        b.Property(x => x.TimezoneBand).HasColumnName("timezone_band").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.Property<List<string>>("_preferredCountries")
            .HasColumnName("preferred_countries")
            .HasColumnType("jsonb")
            .HasConversion(v => StringListJson.Serialize(v), v => StringListJson.Deserialize(v), StringListComparer)
            .IsRequired();

        b.Property<List<EmploymentType>>("_employmentTypes")
            .HasColumnName("employment_types")
            .HasColumnType("jsonb")
            .HasConversion(
                v => EnumListJson.Serialize(v),
                v => EnumListJson.Deserialize<EmploymentType>(v),
                EmploymentTypeListComparer)
            .IsRequired();

        b.Ignore(x => x.PreferredCountries);
        b.Ignore(x => x.EmploymentTypes);

        // uq_profiles_active — one active Profile — is a partial unique index EF cannot model; it is
        // declared in raw SQL in the migration and named here for documentation only.
    }
}
