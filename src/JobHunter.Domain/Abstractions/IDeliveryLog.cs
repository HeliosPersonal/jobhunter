using JobHunter.Domain.Reporting;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The append-only <c>delivery_log</c> — the single mechanism behind [[CONTEXT]] invariant 8
/// ([[adr/0002-delivery-idempotence|ADR-F5-0002]]). It exposes exactly one write, <see cref="TryRecordAsync"/>,
/// and no update or delete path: deleting a row would mean re-delivering, which is precisely the failure the
/// log exists to prevent. A record is written <em>immediately after each successful send</em>, so a crash
/// mid-loop leaves exactly the rows for what already went, and the resumed delivery sends only the rest.
/// </summary>
public interface IDeliveryLog
{
    /// <summary>
    /// Records that a card was delivered, returning <c>true</c> on a genuine insert and <c>false</c> when the
    /// <c>(run_id, chat_id, card_key)</c> row already existed. The unique constraint arbitrates, so two racing
    /// delivery handlers cannot double-send — the loser simply sees <c>false</c>.
    /// </summary>
    Task<bool> TryRecordAsync(DeliveryRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// The card keys already delivered for <paramref name="runId"/> to <paramref name="chatId"/>, so a resumed
    /// delivery can send only the remainder (SAD §6.1). Served by <c>idx_delivery_log_run_chat</c>.
    /// </summary>
    Task<IReadOnlyCollection<string>> DeliveredKeysAsync(
        Guid runId,
        long chatId,
        CancellationToken cancellationToken = default);
}
