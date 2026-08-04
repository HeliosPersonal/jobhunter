using JobHunter.Domain.Common;

namespace JobHunter.Domain.Reporting;

/// <summary>
/// One row of the append-only <c>delivery_log</c> — the single mechanism behind [[CONTEXT]] invariant 8
/// ("one card, one Run, one chat, one delivery", data-model §delivery_log, [[adr/0002-delivery-idempotence|ADR-F5-0002]]).
/// A record is written <em>immediately after each successful send</em>, not after the batch, so a crash
/// after card 7 of 10 leaves exactly seven rows and the resumed delivery sends exactly three.
///
/// <para>There is no update path and no delete path: deleting a row would mean re-delivering, which is
/// precisely the failure the log exists to prevent. The header and footer are recorded with the reserved
/// <see cref="CardKey"/> values and a null <see cref="TelegramMessageId"/>.</para>
/// </summary>
public sealed class DeliveryRecord : Entity
{
    public DeliveryRecord(
        Guid id,
        Guid runId,
        long chatId,
        CardKey cardKey,
        long? telegramMessageId,
        DateTimeOffset deliveredAt)
        : base(id)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A DeliveryRecord must reference a Run.", nameof(runId));
        }

        ArgumentNullException.ThrowIfNull(cardKey);

        RunId = runId;
        ChatId = chatId;
        CardKey = cardKey;
        TelegramMessageId = telegramMessageId;
        DeliveredAt = deliveredAt;
    }

    private DeliveryRecord()
    {
    }

    public Guid RunId { get; private set; }

    public long ChatId { get; private set; }

    /// <summary>Which card this send covers — a job key, or a reserved header/footer key.</summary>
    public CardKey CardKey { get; private set; } = null!;

    /// <summary>The id Telegram assigned the sent message; null for the header and footer.</summary>
    public long? TelegramMessageId { get; private set; }

    public DateTimeOffset DeliveredAt { get; private set; }
}
