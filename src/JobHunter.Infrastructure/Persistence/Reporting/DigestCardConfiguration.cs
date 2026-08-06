using JobHunter.Domain.Jobs;
using JobHunter.Domain.Reporting;
using JobHunter.Infrastructure.Persistence.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Reporting;

/// <summary>
/// Maps the <see cref="DigestCard"/> entity to <c>digest_cards</c> (data-model §digest_cards). Two unique
/// indexes carry the idempotence design: <c>uq_digest_cards_job</c> on <c>(digest_id, job_id)</c> forbids a
/// duplicate card, and <c>uq_digest_cards_key</c> on <c>(digest_id, card_key)</c> lets a callback resolve a
/// short id back to its card. <c>idx_digest_cards_rank</c> serves the ordered render. The
/// <see cref="CardKey"/> persists as its 16-hex text value; <c>reasons</c> is non-empty by construction
/// (invariant 4 lives in the aggregate) and stores as <c>jsonb</c>.
/// </summary>
internal sealed class DigestCardConfiguration : IEntityTypeConfiguration<DigestCard>
{
    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        list => list.ToList());

    private static readonly ValueComparer<List<Guid>> GuidListComparer = new(
        (left, right) => (left ?? new List<Guid>()).SequenceEqual(right ?? new List<Guid>()),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<DigestCard> b)
    {
        b.ToTable("digest_cards");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.DigestId).HasColumnName("digest_id").IsRequired();
        b.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        b.Property(x => x.Rank).HasColumnName("rank").HasColumnType("smallint").IsRequired();
        b.Property(x => x.Score).HasColumnName("score").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(x => x.ApplyUrlVerified).HasColumnName("apply_url_verified").IsRequired();

        b.Property(x => x.Key)
            .HasColumnName("card_key")
            .HasConversion(k => k.Value, v => CardKey.TryCreate(v).Value)
            .IsRequired();

        b.Property<List<string>>("_reasons")
            .HasColumnName("reasons")
            .HasColumnType("jsonb")
            .HasConversion(v => StringListJson.Serialize(v), v => StringListJson.Deserialize(v), StringListComparer)
            .IsRequired();

        // The near-duplicate jobs this card groups away (F5-T13). A jsonb array, empty for a card that groups
        // nothing, so grouped-away jobs remain queryable and are snapshotted onto the digest like everything else.
        b.Property<List<Guid>>("_groupedJobIds")
            .HasColumnName("grouped_job_ids")
            .HasColumnType("jsonb")
            .HasConversion(v => GuidListJson.Serialize(v), v => GuidListJson.Deserialize(v), GuidListComparer)
            .IsRequired();

        b.Ignore(x => x.Reasons);
        b.Ignore(x => x.GroupedJobIds);

        b.HasOne<Job>()
            .WithMany()
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.DigestId, x.JobId }).IsUnique().HasDatabaseName("uq_digest_cards_job");
        b.HasIndex(x => new { x.DigestId, x.Key }).IsUnique().HasDatabaseName("uq_digest_cards_key");
        b.HasIndex(x => new { x.DigestId, x.Rank }).HasDatabaseName("idx_digest_cards_rank");
    }
}
