using JobHunter.Domain.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Research;

/// <summary>
/// Maps the <see cref="ResearchSource"/> child to <c>research_sources</c> (F8 data-model §research_sources) —
/// the citation authority, every document fetched and stored before synthesis. <c>uq_sources_url</c> on
/// <c>(research_id, url)</c> keeps one row per fetched URL within a dossier, and <c>idx_sources_research</c>
/// on <c>(research_id, category)</c> serves citation verification. The <c>research_id</c> shadow foreign key
/// is owned by the parent aggregate's <c>HasMany</c>; a <c>(research_id, id)</c> unique key is what the
/// claims' composite foreign key references, so a claim can only cite a source in its own dossier.
/// </summary>
internal sealed class ResearchSourceConfiguration : IEntityTypeConfiguration<ResearchSource>
{
    public void Configure(EntityTypeBuilder<ResearchSource> b)
    {
        b.ToTable("research_sources");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property<Guid>("research_id").HasColumnName("research_id");
        b.Property(x => x.Category).HasColumnName("category").IsRequired();
        b.Property(x => x.Url).HasColumnName("url").IsRequired();
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.TextLength).HasColumnName("text_length").IsRequired();
        b.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();

        // The target of the claims' composite foreign key: a source is uniquely identified within its dossier
        // by (research_id, id), so a claim citing (its research_id, source_id) can only resolve to a source in
        // the same dossier — a source from another dossier is unrepresentable (invariant 5).
        b.HasAlternateKey("research_id", nameof(ResearchSource.Id));

        b.HasIndex("research_id", "Category").HasDatabaseName("idx_sources_research");
        b.HasIndex("research_id", nameof(ResearchSource.Url))
            .IsUnique()
            .HasDatabaseName("uq_sources_url");
    }
}
