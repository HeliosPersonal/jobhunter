using JobHunter.Domain.Notifications;

namespace JobHunter.Domain.Abstractions;

/// <summary>
/// The one boundary to the Owner's chat (SAD §5). A domain port, so the digest can be rendered and its
/// ordering asserted against a fake notifier with no Telegram present — which is what makes the rendering
/// corpus fast enough to run on every PR. The single implementation, <c>TelegramNotifier</c>, paces to
/// stay inside Telegram's rate limits and honours a <c>429 retry_after</c> exactly; the bot token lives
/// only in that adapter's HTTP base address and never reaches this port, a log, an exception or a span
/// (invariant 12).
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Sends one rendered message to <paramref name="chatId"/> and returns the Telegram message id, which
    /// the delivery log records so a redelivery is a no-op (invariant 8).
    /// </summary>
    Task<long> SendAsync(long chatId, RenderedMessage message, CancellationToken cancellationToken = default);
}
