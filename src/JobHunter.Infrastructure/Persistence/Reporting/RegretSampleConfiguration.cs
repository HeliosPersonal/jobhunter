using JobHunter.Domain.Ratings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Reporting;

/// <summary>
/// Maps the <see cref="RegretSample"/> to <c>regret_sample_log</c> (F4 T21, ADR-F4-0003). The unique
/// <c>uq_regret_sample</c> on <c>week_start</c> <em>is</em> the per-week idempotence of the regret sampler,
/// exactly as <c>uq_rating_round</c> is for the rating loop. The table is append-only — the repository exposes
/// no update or delete — and this mapping exists so the migration creates the table and its constraint; the
/// write goes through a raw <c>ON CONFLICT DO NOTHING</c> insert.
/// </summary>
internal sealed class RegretSampleConfiguration : IEntityTypeConfiguration<RegretSample>
{
    public void Configure(EntityTypeBuilder<RegretSample> b)
    {
        b.ToTable("regret_sample_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.WeekStart).HasColumnName("week_start").IsRequired();
        b.Property(x => x.OpenedAt).HasColumnName("opened_at").IsRequired();

        b.HasIndex(x => x.WeekStart)
            .IsUnique()
            .HasDatabaseName("uq_regret_sample");
    }
}
