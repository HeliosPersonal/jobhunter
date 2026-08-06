using JobHunter.Domain.Notifications;

namespace JobHunter.Telegram.Callbacks;

/// <summary>
/// The two Telegram control calls a callback needs after a tap: acknowledge the query so the Owner's tap
/// stops spinning, and rewrite the tapped card's keyboard (contract §Callback payloads). It is the narrow
/// seam between <see cref="CallbackHandler"/> and the concrete <c>TelegramNotifier</c> — the handler depends
/// on this, not on the whole notifier, so it is unit-tested with a recording spy and no HTTP. Internal
/// because the whole bot host is internal; <c>TelegramNotifier</c> already carries both methods.
/// </summary>
internal interface ICallbackResponder
{
    /// <summary>
    /// Acknowledges the callback query <paramref name="callbackQueryId"/>, optionally showing
    /// <paramref name="text"/> as a toast. The Open action passes no text; the URL button opens directly.
    /// </summary>
    Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken cancellationToken = default);

    /// <summary>Replaces the inline keyboard of the already-sent card at <paramref name="messageId"/>.</summary>
    Task EditReplyMarkupAsync(
        long chatId,
        long messageId,
        IReadOnlyList<IReadOnlyList<InlineButton>> keyboard,
        CancellationToken cancellationToken = default);
}
