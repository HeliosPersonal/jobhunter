using JobHunter.Domain.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Research;

/// <summary>
/// Maps the <see cref="ResearchClaim"/> child to <c>research_claims</c> (F8 data-model §research_claims) —
/// where invariant 5 lives in the schema. <c>source_id</c> is <c>NOT NULL</c>, so an uncited claim cannot be
/// inserted, and a composite foreign key <c>(research_id, source_id)</c> to <c>research_sources(research_id,
/// id)</c> — declared in the migration's raw SQL, because EF has no navigation to model it — rejects a claim
/// citing a source from another dossier. <c>idx_claims_research</c> on <c>(research_id, category)</c> serves
/// rendering grouped by category; <c>idx_claims_warnings</c>, partial on <c>is_warning</c>, surfaces warnings
/// first (AC-04).
/// </summary>
internal sealed class ResearchClaimConfiguration : IEntityTypeConfiguration<ResearchClaim>
{
    public void Configure(EntityTypeBuilder<ResearchClaim> b)
    {
        b.ToTable("research_claims");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property<Guid>("research_id").HasColumnName("research_id");
        b.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        b.Property(x => x.Category).HasColumnName("category").IsRequired();
        b.Property(x => x.Claim).HasColumnName("claim").IsRequired();
        b.Property(x => x.IsWarning).HasColumnName("is_warning").IsRequired();
        b.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();

        b.HasIndex("research_id", "Category").HasDatabaseName("idx_claims_research");
        b.HasIndex("research_id")
            .HasDatabaseName("idx_claims_warnings")
            .HasFilter("is_warning");
    }
}
