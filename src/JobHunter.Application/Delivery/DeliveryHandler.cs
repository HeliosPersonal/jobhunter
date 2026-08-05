using JobHunter.Contracts.Pipeline;
using JobHunter.Domain.Abstractions;
using JobHunter.Domain.Notifications;
using JobHunter.Domain.Reporting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace JobHunter.Application.Delivery;

/// <summary>
/// Delivers a Run's digest to the Owner, exactly once per card (F5 SAD §6.1, QG-2,
/// [[adr/0002-delivery-idempotence|ADR-F5-0002]]). Consumes <see cref="DigestReady"/>, loads the persisted
/// digest, renders it into the ordered message sequence (header, cards by rank, footer), asks the
/// <c>delivery_log</c> which card keys already went to this chat, and sends only the remainder — writing a
/// log row <em>immediately after each successful send</em>, never after the batch.
///
/// <para>The ordering is the whole point (ADR-F5-0002): send, then record. A crash in the one-statement
/// window between the two re-sends that single card on resume — an at-least-once send with a one-card
/// duplicate, chosen deliberately over an at-most-once send that could drop a card the Owner cannot detect
/// is missing. Two racing handlers cannot double-send: <see cref="IDeliveryLog.TryRecordAsync"/> returns
/// <c>false</c> for the loser on the unique <c>(run_id, chat_id, card_key)</c> constraint, and a
/// <c>false</c> is a no-op, not an error. A permanent per-card rejection (a Telegram 400,
/// <see cref="NotificationRejectedException"/>) logs that one card as failed and the loop delivers the rest;
/// a transient fault propagates so Wolverine redelivers and the loop resumes from the log.</para>
///
/// <para>It is discovered and constructed by Wolverine like every other handler; it reads only stored digest
/// state and sends public card text — the CV crosses exactly one boundary, and it is not this one.</para>
/// </summary>
public sealed class DeliveryHandler(
    IDigestRepository digests,
    IDigestRenderer renderer,
    IDeliveryLog deliveryLog,
    INotifier notifier,
    IIdGenerator ids,
    IClock clock,
    DeliveryOptions options,
    ILogger<DeliveryHandler> logger)
{
    private readonly IDigestRepository _digests = digests ?? throw new ArgumentNullException(nameof(digests));
    private readonly IDigestRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly IDeliveryLog _deliveryLog = deliveryLog ?? throw new ArgumentNullException(nameof(deliveryLog));
    private readonly INotifier _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    private readonly IIdGenerator _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly DeliveryOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<DeliveryHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task Handle(DigestReady message, IMessageBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(bus);

        var digest = await _digests.FindByRunAsync(message.RunId, cancellationToken).ConfigureAwait(false);
        if (digest is null)
        {
            // DigestReady is published only after the digest is committed (S2); a missing one means a race we
            // cannot recover from by guessing. Surface it so Wolverine redelivers once the write is visible.
            _logger.LogWarning("DigestReady for Run {RunId} but no persisted digest was found; retrying.", message.RunId);
            throw new InvalidOperationException($"No persisted digest for Run {message.RunId}.");
        }

        var chatId = _options.OwnerChatId;
        var messages = await _renderer.RenderAsync(digest, cancellationToken).ConfigureAwait(false);

        // Load what already went to this chat for this Run, so a resumed delivery sends only the remainder.
        var alreadyDelivered = await _deliveryLog.DeliveredKeysAsync(message.RunId, chatId, cancellationToken)
            .ConfigureAwait(false);
        var delivered = new HashSet<string>(alreadyDelivered, StringComparer.Ordinal);

        var sent = 0;
        var failed = 0;

        foreach (var renderable in messages)
        {
            if (delivered.Contains(renderable.Key.Value))
            {
                // Already sent on an earlier pass — skip without sending, so redelivery re-sends nothing (QG-2).
                continue;
            }

            long? telegramMessageId;
            try
            {
                telegramMessageId = await _notifier.SendAsync(chatId, renderable.Message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (NotificationRejectedException ex)
            {
                // A permanent per-card refusal (a 400): log the one card as failed and keep delivering the rest
                // (AC-05). No log row is written, so a future re-render with a valid message can still deliver it.
                failed++;
                _logger.LogWarning(
                    ex,
                    "Card {CardKey} in Run {RunId} was permanently rejected; delivering the rest.",
                    renderable.Key.Value, message.RunId);
                continue;
            }

            // Record immediately after the successful send (S1, ADR-F5-0002). The unique constraint arbitrates a
            // race: a false means another handler already logged this card, so we simply do not double-count it.
            var record = new DeliveryRecord(
                _ids.NewId(), message.RunId, chatId, renderable.Key, telegramMessageId, _clock.UtcNow);
            var recorded = await _deliveryLog.TryRecordAsync(record, cancellationToken).ConfigureAwait(false);

            delivered.Add(renderable.Key.Value);
            if (recorded)
            {
                sent++;
            }
        }

        await bus.PublishAsync(new DigestDelivered(message.RunId, chatId, sent, failed, _clock.UtcNow))
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Delivered digest for Run {RunId} to chat {ChatId}: {Sent} messages sent, {Failed} cards failed.",
            message.RunId, chatId, sent, failed);
    }
}
