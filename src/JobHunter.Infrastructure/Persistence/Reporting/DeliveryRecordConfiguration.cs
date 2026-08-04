using JobHunter.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Infrastructure.Persistence.Reporting;

/// <summary>
/// Maps the <see cref="DeliveryRecord"/> to <c>delivery_log</c> (data-model §delivery_log). The unique
/// <c>uq_delivery_log</c> on <c>(run_id, chat_id, card_key)</c> <em>is</em> [[CONTEXT]] invariant 8
/// ([[adr/0002-delivery-idempotence|ADR-F5-0002]]); <c>idx_delivery_log_run_chat</c> serves the
/// "what have I already sent" read on resume. The table is append-only — the repository exposes no update
/// or delete — and this mapping exists so the migration creates the table and its constraints; writes go
/// through a raw <c>ON CONFLICT DO NOTHING</c> upsert.
/// </summary>
internal sealed class DeliveryRecordConfiguration : IEntityTypeConfiguration<DeliveryRecord>
{
    public void Configure(EntityTypeBuilder<DeliveryRecord> b)
    {
        b.ToTable("delivery_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        b.Property(x => x.ChatId).HasColumnName("chat_id").IsRequired();

        b.Property(x => x.CardKey)
            .HasColumnName("card_key")
            .HasConversion(k => k.Value, v => CardKey.TryCreate(v).Value)
            .IsRequired();

        b.Property(x => x.TelegramMessageId).HasColumnName("telegram_message_id");
        b.Property(x => x.DeliveredAt).HasColumnName("delivered_at").IsRequired();

        b.HasOne<Domain.Pipeline.Run>()
            .WithMany()
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.RunId, x.ChatId, x.CardKey })
            .IsUnique()
            .HasDatabaseName("uq_delivery_log");

        b.HasIndex(x => new { x.RunId, x.ChatId })
            .HasDatabaseName("idx_delivery_log_run_chat");
    }
}
