using JobHunter.Domain.Ratings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Reporting;

/// <summary>
/// Maps the <see cref="RatingRound"/> to <c>rating_round_log</c> (F4 T20). The unique <c>uq_rating_round</c> on
/// <c>(week_start, chat_id)</c> <em>is</em> the per-week idempotence of the rating loop, exactly as
/// <c>uq_delivery_log</c> is invariant 8 for delivery. The table is append-only — the repository exposes no
/// update or delete — and this mapping exists so the migration creates the table and its constraint; the write
/// goes through a raw <c>ON CONFLICT DO NOTHING</c> insert.
/// </summary>
internal sealed class RatingRoundConfiguration : IEntityTypeConfiguration<RatingRound>
{
    public void Configure(EntityTypeBuilder<RatingRound> b)
    {
        b.ToTable("rating_round_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.WeekStart).HasColumnName("week_start").IsRequired();
        b.Property(x => x.ChatId).HasColumnName("chat_id").IsRequired();
        b.Property(x => x.OpenedAt).HasColumnName("opened_at").IsRequired();

        b.HasIndex(x => new { x.WeekStart, x.ChatId })
            .IsUnique()
            .HasDatabaseName("uq_rating_round");
    }
}
