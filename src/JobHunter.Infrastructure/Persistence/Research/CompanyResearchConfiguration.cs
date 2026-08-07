using JobHunter.Domain.Companies;
using JobHunter.Domain.Pipeline;
using JobHunter.Domain.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Research;

/// <summary>
/// Maps the <see cref="CompanyResearch"/> aggregate to <c>company_research</c> (F8 data-model
/// §company_research). Its sources and claims are owned children written through the same insert, so a dossier
/// is stored whole. <c>uq_research_company_run</c> enforces one dossier per <c>(company, run)</c>, and
/// <c>idx_research_company_latest</c> on <c>(company_id, generated_at DESC)</c> serves the newest-dossier read
/// a freshness check turns on. <c>categories_unavailable</c> persists as <c>jsonb</c> — recording absence
/// explicitly is what lets the dossier say "no engineering blog found" (AC-07); <c>categories_covered</c> is
/// derived from the claims by the aggregate, never stored, so it cannot disagree with what was asserted.
/// </summary>
internal sealed class CompanyResearchConfiguration : IEntityTypeConfiguration<CompanyResearch>
{
    private static readonly ValueComparer<List<ResearchCategory>> CategoryListComparer = new(
        (left, right) => (left ?? new List<ResearchCategory>()).SequenceEqual(right ?? new List<ResearchCategory>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<CompanyResearch> b)
    {
        b.ToTable("company_research");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        b.Property(x => x.Summary).HasColumnName("summary").IsRequired();
        b.Property(x => x.ClaimsDiscarded).HasColumnName("claims_discarded").IsRequired();
        b.Property(x => x.PromptVersion).HasColumnName("prompt_version").IsRequired();
        b.Property(x => x.GeneratedAt).HasColumnName("generated_at").IsRequired();

        // categories_covered is derived by the aggregate from its claims (T01), never a stored field — so it
        // is a shadow jsonb column the repository denormalises at write time (for Dapper reads: F5 digest,
        // F9 facets). A read never sets it back: the loaded claims re-derive CategoriesCovered, so the column
        // can never disagree with what was asserted.
        b.Property<string>("categories_covered")
            .HasColumnName("categories_covered")
            .HasColumnType("jsonb")
            .IsRequired();

        b.Property<List<ResearchCategory>>("_categoriesUnavailable")
            .HasColumnName("categories_unavailable")
            .HasColumnType("jsonb")
            .HasConversion(v => ResearchCategoryListJson.Serialize(v), v => ResearchCategoryListJson.Deserialize(v), CategoryListComparer)
            .IsRequired();
        b.Ignore(x => x.CategoriesUnavailable);

        b.HasMany(x => x.Sources)
            .WithOne()
            .HasForeignKey("research_id")
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Sources).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(x => x.Claims)
            .WithOne()
            .HasForeignKey("research_id")
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Claims).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Run>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.CompanyId, x.RunId })
            .IsUnique()
            .HasDatabaseName("uq_research_company_run");
        b.HasIndex(x => new { x.CompanyId, x.GeneratedAt })
            .HasDatabaseName("idx_research_company_latest")
            .IsDescending(false, true);
    }
}
